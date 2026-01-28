using System;
using System.Runtime.InteropServices;
using System.Text;
using SendAlerts.Core.Interfaces;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// Windows 平台 NvAPI GPU 資料提供者
/// 讀取 GPU 功耗和溫度
/// </summary>
public class NvApiWindowsProvider : IGpuProvider
{
    private const string NvApiDll = "nvapi64.dll";
    private const string NvmlDll = "nvml.dll";

    private bool _isInitialized;
    private IntPtr _gpuHandle;
    private string _gpuName = "Unknown";
    private float _powerLimit;

    // NVML for power reading
    private bool _nvmlInitialized;
    private IntPtr _nvmlDeviceHandle;

    // NvAPI Status codes
    private const int NVAPI_OK = 0;

    // NvAPI Function IDs (hashed)
    private const uint NvAPI_Initialize_ID = 0x0150E828;
    private const uint NvAPI_Unload_ID = 0xD22BDD7E;
    private const uint NvAPI_EnumPhysicalGPUs_ID = 0xE5AC921F;
    private const uint NvAPI_GPU_GetFullName_ID = 0xCEEE8E9F;
    private const uint NvAPI_GPU_GetThermalSettings_ID = 0xE3640A56;
    private const uint NvAPI_GPU_GetDynamicPstatesInfoEx_ID = 0x60DED2ED;

    // Thermal sensor target
    private const int NVAPI_THERMAL_TARGET_ALL = 15;
    private const int NVAPI_THERMAL_TARGET_GPU = 1;

    // Maximum values
    private const int NVAPI_MAX_PHYSICAL_GPUS = 64;
    private const int NVAPI_MAX_THERMAL_SENSORS_PER_GPU = 3;
    private const int NV_GPU_SHORT_STRING_LENGTH = 64;

    // --- NvAPI 結構定義 ---

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_THERMAL_SETTINGS_V2
    {
        public uint Version;
        public uint Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NVAPI_MAX_THERMAL_SENSORS_PER_GPU)]
        public NV_THERMAL_SENSOR[] Sensor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_THERMAL_SENSOR
    {
        public int Controller;
        public int DefaultMinTemp;
        public int DefaultMaxTemp;
        public int CurrentTemp;
        public int Target;
    }

    // NV_GPU_DYNAMIC_PSTATES_INFO_EX 結構
    private const int NVAPI_MAX_GPU_UTILIZATIONS = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_DYNAMIC_PSTATES_INFO_EX
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NVAPI_MAX_GPU_UTILIZATIONS)]
        public NV_GPU_UTILIZATION_DOMAIN[] Utilization;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_UTILIZATION_DOMAIN
    {
        public uint IsPresent;    // 1 = present, 0 = not present
        public uint Percentage;   // 使用率百分比 (0-100)
    }

    // --- P/Invoke 宣告 ---

    [DllImport(NvApiDll, EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint interfaceId);

    // --- NVML P/Invoke ---
    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit_v2();

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint limit);

    // 委派定義
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_UnloadDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_EnumPhysicalGPUsDelegate(
        [MarshalAs(UnmanagedType.LPArray, SizeConst = NVAPI_MAX_PHYSICAL_GPUS)] IntPtr[] gpuHandles,
        out int gpuCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetFullNameDelegate(
        IntPtr hPhysicalGpu,
        StringBuilder szName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetThermalSettingsDelegate(
        IntPtr hPhysicalGpu,
        int sensorIndex,
        ref NV_GPU_THERMAL_SETTINGS_V2 thermalSettings);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetDynamicPstatesInfoExDelegate(
        IntPtr hPhysicalGpu,
        ref NV_GPU_DYNAMIC_PSTATES_INFO_EX pDynamicPstatesInfoEx);

    // 函數指標
    private NvAPI_InitializeDelegate? _nvApiInitialize;
    private NvAPI_UnloadDelegate? _nvApiUnload;
    private NvAPI_EnumPhysicalGPUsDelegate? _nvApiEnumPhysicalGPUs;
    private NvAPI_GPU_GetFullNameDelegate? _nvApiGetFullName;
    private NvAPI_GPU_GetThermalSettingsDelegate? _nvApiGetThermalSettings;
    private NvAPI_GPU_GetDynamicPstatesInfoExDelegate? _nvApiGetDynamicPstatesInfoEx;

    public bool IsAvailable => _isInitialized;
    public float PowerLimit => _powerLimit;

    // 硬體模式與動態標籤 (GPU 模式)
    public HardwareMode Mode => HardwareMode.Gpu;
    public string PrimaryMetricLabel => "GPU Utilization";
    public string TemperatureLabel => "GPU Temperature";
    public string SecondaryMetricLabel => "Power Usage";
    public string SecondaryMetricUnit => "W";

    public NvApiWindowsProvider()
    {
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Log.Information("[NvAPI] 開始初始化...");

            // 查詢函數指標
            if (!QueryInterfaces())
            {
                Log.Error("[NvAPI] 無法查詢 NvAPI 介面");
                return;
            }

            // 初始化 NvAPI
            var result = _nvApiInitialize!();
            if (result != NVAPI_OK)
            {
                Log.Error("[NvAPI] NvAPI_Initialize 失敗，錯誤碼: {ErrorCode}", result);
                return;
            }

            // 列舉 GPU
            var gpuHandles = new IntPtr[NVAPI_MAX_PHYSICAL_GPUS];
            result = _nvApiEnumPhysicalGPUs!(gpuHandles, out int gpuCount);
            if (result != NVAPI_OK || gpuCount == 0)
            {
                Log.Error("[NvAPI] NvAPI_EnumPhysicalGPUs 失敗，錯誤碼: {ErrorCode}, GPU 數量: {Count}", result, gpuCount);
                return;
            }

            _gpuHandle = gpuHandles[0];

            // 取得 GPU 名稱
            var nameBuilder = new StringBuilder(NV_GPU_SHORT_STRING_LENGTH);
            result = _nvApiGetFullName!(_gpuHandle, nameBuilder);
            if (result == NVAPI_OK)
            {
                _gpuName = nameBuilder.ToString();
            }

            // 初始化 NVML 用於讀取功耗
            InitializeNvml();

            _isInitialized = true;
            Log.Information("[NvAPI] 初始化完成 | GPU: {GpuName} | PowerLimit: {PowerLimit:F1}W", _gpuName, _powerLimit);
        }
        catch (DllNotFoundException ex)
        {
            Log.Error(ex, "[NvAPI] 找不到 nvapi64.dll");
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NvAPI] 初始化時發生未預期的錯誤");
            _isInitialized = false;
        }
    }

    private bool QueryInterfaces()
    {
        try
        {
            var ptrInitialize = NvAPI_QueryInterface(NvAPI_Initialize_ID);
            var ptrUnload = NvAPI_QueryInterface(NvAPI_Unload_ID);
            var ptrEnumGPUs = NvAPI_QueryInterface(NvAPI_EnumPhysicalGPUs_ID);
            var ptrGetFullName = NvAPI_QueryInterface(NvAPI_GPU_GetFullName_ID);
            var ptrGetThermal = NvAPI_QueryInterface(NvAPI_GPU_GetThermalSettings_ID);
            var ptrGetDynamicPstates = NvAPI_QueryInterface(NvAPI_GPU_GetDynamicPstatesInfoEx_ID);

            if (ptrInitialize == IntPtr.Zero || ptrEnumGPUs == IntPtr.Zero)
            {
                Log.Error("[NvAPI] 無法查詢基本介面");
                return false;
            }

            _nvApiInitialize = Marshal.GetDelegateForFunctionPointer<NvAPI_InitializeDelegate>(ptrInitialize);
            _nvApiUnload = ptrUnload != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_UnloadDelegate>(ptrUnload)
                : null;
            _nvApiEnumPhysicalGPUs = Marshal.GetDelegateForFunctionPointer<NvAPI_EnumPhysicalGPUsDelegate>(ptrEnumGPUs);
            _nvApiGetFullName = ptrGetFullName != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetFullNameDelegate>(ptrGetFullName)
                : null;
            _nvApiGetThermalSettings = ptrGetThermal != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetThermalSettingsDelegate>(ptrGetThermal)
                : null;
            _nvApiGetDynamicPstatesInfoEx = ptrGetDynamicPstates != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetDynamicPstatesInfoExDelegate>(ptrGetDynamicPstates)
                : null;

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NvAPI] 查詢介面時發生錯誤");
            return false;
        }
    }

    private void InitializeNvml()
    {
        try
        {
            if (nvmlInit_v2() == 0)
            {
                if (nvmlDeviceGetHandleByIndex_v2(0, out _nvmlDeviceHandle) == 0)
                {
                    _nvmlInitialized = true;

                    // 取得功耗限制
                    if (nvmlDeviceGetPowerManagementLimit(_nvmlDeviceHandle, out uint limitMw) == 0)
                    {
                        _powerLimit = limitMw / 1000.0f;
                    }

                    Log.Information("[NvAPI] NVML 初始化成功");
                }
            }
        }
        catch (DllNotFoundException)
        {
            Log.Warning("[NvAPI] 找不到 nvml.dll");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NvAPI] NVML 初始化失敗");
        }
    }

    public string GetGpuName() => _isInitialized ? _gpuName : "N/A";

    public GpuReading GetCurrentReading()
    {
        if (!_isInitialized)
            return new GpuReading(0, 0, 0, DateTime.Now);

        float gpuUtilization = GetGpuUtilization();
        float temperature = GetTemperature();
        float powerUsage = GetPowerUsage();

        return new GpuReading(gpuUtilization, temperature, powerUsage, DateTime.Now);
    }

    private float GetGpuUtilization()
    {
        if (_nvApiGetDynamicPstatesInfoEx == null)
            return 0;

        try
        {
            var pstatesInfo = new NV_GPU_DYNAMIC_PSTATES_INFO_EX
            {
                Version = (uint)(Marshal.SizeOf<NV_GPU_DYNAMIC_PSTATES_INFO_EX>() | (1 << 16)),
                Utilization = new NV_GPU_UTILIZATION_DOMAIN[NVAPI_MAX_GPU_UTILIZATIONS]
            };

            var result = _nvApiGetDynamicPstatesInfoEx(_gpuHandle, ref pstatesInfo);
            if (result == NVAPI_OK)
            {
                // Domain 0 = GPU Core utilization
                if (pstatesInfo.Utilization[0].IsPresent == 1)
                {
                    return pstatesInfo.Utilization[0].Percentage;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NvAPI] 讀取 GPU 使用率失敗");
        }

        return 0;
    }

    private float GetTemperature()
    {
        if (_nvApiGetThermalSettings == null)
            return 0;

        try
        {
            var thermalSettings = new NV_GPU_THERMAL_SETTINGS_V2
            {
                Version = (uint)(Marshal.SizeOf<NV_GPU_THERMAL_SETTINGS_V2>() | (2 << 16)),
                Sensor = new NV_THERMAL_SENSOR[NVAPI_MAX_THERMAL_SENSORS_PER_GPU]
            };

            // 使用 NVAPI_THERMAL_TARGET_ALL 取得所有感測器
            var result = _nvApiGetThermalSettings(_gpuHandle, NVAPI_THERMAL_TARGET_ALL, ref thermalSettings);
            if (result == NVAPI_OK && thermalSettings.Count > 0)
            {
                // 優先尋找 GPU 核心溫度
                for (int i = 0; i < thermalSettings.Count && i < NVAPI_MAX_THERMAL_SENSORS_PER_GPU; i++)
                {
                    if (thermalSettings.Sensor[i].Target == NVAPI_THERMAL_TARGET_GPU &&
                        thermalSettings.Sensor[i].CurrentTemp > 0)
                    {
                        return thermalSettings.Sensor[i].CurrentTemp;
                    }
                }

                // 返回第一個有效溫度
                for (int i = 0; i < thermalSettings.Count && i < NVAPI_MAX_THERMAL_SENSORS_PER_GPU; i++)
                {
                    if (thermalSettings.Sensor[i].CurrentTemp > 0)
                    {
                        return thermalSettings.Sensor[i].CurrentTemp;
                    }
                }
            }
            else
            {
                // 嘗試使用 sensorIndex = 0
                result = _nvApiGetThermalSettings(_gpuHandle, 0, ref thermalSettings);
                if (result == NVAPI_OK && thermalSettings.Count > 0 && thermalSettings.Sensor[0].CurrentTemp > 0)
                {
                    return thermalSettings.Sensor[0].CurrentTemp;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NvAPI] 讀取溫度失敗");
        }

        return 0;
    }

    private float GetPowerUsage()
    {
        if (!_nvmlInitialized)
            return 0;

        try
        {
            if (nvmlDeviceGetPowerUsage(_nvmlDeviceHandle, out uint powerMw) == 0)
            {
                return powerMw / 1000.0f; // mW -> W
            }
        }
        catch
        {
            // 忽略錯誤
        }

        return 0;
    }

    public void Dispose()
    {
        // 關閉 NVML
        if (_nvmlInitialized)
        {
            try
            {
                nvmlShutdown();
            }
            catch { }
            _nvmlInitialized = false;
        }

        // 關閉 NvAPI
        if (_isInitialized && _nvApiUnload != null)
        {
            try
            {
                _nvApiUnload();
                Log.Information("[NvAPI] 已安全卸載");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NvAPI] 卸載時發生錯誤");
            }
        }
        _isInitialized = false;
    }
}
