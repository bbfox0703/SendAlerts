using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using _16pin_vmon.Core.Interfaces;
using Serilog;

namespace _16pin_vmon.Desktop.Implementations;

/// <summary>
/// Windows 平台 NvAPI GPU 資料提供者
/// 作為 NVML 無法讀取電壓時的 Fallback
/// 使用 nvapi64.dll P/Invoke
/// </summary>
public class NvApiWindowsProvider : IGpuProvider
{
    private const string NvApiDll = "nvapi64.dll";
    private const string NvmlDll = "nvml.dll";

    private bool _isInitialized;
    private IntPtr _gpuHandle;
    private string _gpuName = "Unknown";
    private float _lastVoltage;

    // NVML for GPU Core Voltage reading (Field 169)
    private bool _nvmlInitialized;
    private IntPtr _nvmlDeviceHandle;
    private const uint NVML_FIELD_GPU_CORE_VOLTAGE = 169;

    // NvAPI Status codes
    private const int NVAPI_OK = 0;
    private const int NVAPI_ERROR = -1;
    private const int NVAPI_LIBRARY_NOT_FOUND = -2;
    private const int NVAPI_NO_IMPLEMENTATION = -3;
    private const int NVAPI_API_NOT_INITIALIZED = -4;
    private const int NVAPI_INVALID_ARGUMENT = -5;
    private const int NVAPI_NVIDIA_DEVICE_NOT_FOUND = -6;

    // NvAPI Function IDs (hashed) - 從 nvapi.lib 導出
    private const uint NvAPI_Initialize_ID = 0x0150E828;
    private const uint NvAPI_Unload_ID = 0xD22BDD7E;
    private const uint NvAPI_EnumPhysicalGPUs_ID = 0xE5AC921F;
    private const uint NvAPI_GPU_GetFullName_ID = 0xCEEE8E9F;
    private const uint NvAPI_GPU_GetThermalSettings_ID = 0xE3640A56;
    private const uint NvAPI_GPU_GetPstates20_ID = 0x6FF81213;
    private const uint NvAPI_GPU_GetAllClockFrequencies_ID = 0xDCB616C3;
    private const uint NvAPI_GPU_GetVoltageDomainsStatus_ID = 0xC16C7E2C;
    private const uint NvAPI_GPU_GetCurrentPstate_ID = 0x927DA4F6;
    private const uint NvAPI_GPU_GetDynamicPstatesInfoEx_ID = 0x60DED2ED;
    private const uint NvAPI_GPU_ClientPowerPoliciesGetStatus_ID = 0x70916171;
    private const uint NvAPI_GPU_GetVoltageStep_ID = 0x28766157;  // 可能的電壓步進 API
    private const uint NvAPI_GPU_GetVoltages_ID = 0x7D656244;     // 可能的電壓 API
    private const uint NvAPI_GPU_GetPowerSensors_ID = 0x271C11F3; // 電源感測器 API (最穩定的 16-pin 讀取方式)

    // 額外的可能電壓/電源相關 API (從 HWiNFO, GPU-Z 等工具分析得來)
    private const uint NvAPI_GPU_GetPowerStatus_ID = 0x70916171;   // ClientPowerPoliciesGetStatus
    private const uint NvAPI_GPU_GetCurrentVoltage_ID = 0x465F9BCF; // 可能的直接電壓 API
    private const uint NvAPI_GPU_GetVoltageStatus_ID = 0x0DBF1DB5;  // 另一個電壓狀態 API
    private const uint NvAPI_GPU_GetPowerMonitorStatus_ID = 0xC12F3C90; // 電源監控
    private const uint NvAPI_GPU_GetPowerTopologyStatus_ID = 0xE2218E38; // 電源拓撲
    private const uint NvAPI_GPU_GetPowerMizerInfo_ID = 0x76BFA16B;  // PowerMizer
    private const uint NvAPI_GPU_GetRailVoltage_ID = 0x261F10CF;    // 可能的電壓軌 API
    private const uint NvAPI_GPU_GetPerfSensors_ID = 0xFB85E0B0;    // 效能感測器

    // Thermal sensor target
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

    // =====================================================================
    // NV_GPU_PERF_PSTATES20 結構 - 根據 NVAPI R590 官方文件定義
    // 參考: NVAPI_Reference_OpenSource/group__gpupstate.html
    // =====================================================================
    // 常數定義 (來自官方文件)
    private const int NVAPI_MAX_GPU_PSTATE20_PSTATES = 16;
    private const int NVAPI_MAX_GPU_PSTATE20_CLOCKS = 8;
    private const int NVAPI_MAX_GPU_PSTATE20_BASE_VOLTAGES = 4;

    /// <summary>
    /// NV_GPU_PERF_PSTATES20_PARAM_DELTA - 用於電壓/頻率差值
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_PERF_PSTATES20_PARAM_DELTA
    {
        public int Value;      // 當前差值
        public int Min;        // 最小差值
        public int Max;        // 最大差值
    }

    /// <summary>
    /// NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY_V1 - 單一電壓域條目
    /// 根據官方文件:
    /// - domainId: NV_GPU_PERF_VOLTAGE_INFO_DOMAIN_ID
    /// - bIsEditable:1 + reserved:31 (位域)
    /// - volt_uV: 電壓 (微伏)
    /// - voltDelta_uV: NV_GPU_PERF_PSTATES20_PARAM_DELTA
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY_V1
    {
        public uint DomainId;                           // 電壓域 ID
        public uint Flags;                              // bIsEditable:1 + reserved:31
        public uint Volt_uV;                            // 電壓 (微伏)
        public NV_GPU_PERF_PSTATES20_PARAM_DELTA VoltDelta_uV;  // 電壓差值
    }
    // sizeof = 4 + 4 + 4 + 12 = 24 bytes

    /// <summary>
    /// NV_GPU_PSTATE20_CLOCK_ENTRY_V1 - 單一時脈域條目
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_PSTATE20_CLOCK_ENTRY_V1
    {
        public uint DomainId;                           // 時脈域 ID
        public uint TypeId;                             // 類型 (Single/Range)
        public uint Flags;                              // bIsEditable:1 + reserved:31
        public NV_GPU_PSTATE20_CLOCK_FREQ Freq;         // 頻率資訊
    }
    // sizeof = 4 + 4 + 4 + 16 = 28 bytes

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_PSTATE20_CLOCK_FREQ
    {
        public uint Freq_kHz;                           // 頻率 (kHz)
        public NV_GPU_PERF_PSTATES20_PARAM_DELTA FreqDelta_kHz;  // 頻率差值
    }
    // sizeof = 4 + 12 = 16 bytes

    // NV_GPU_DYNAMIC_PSTATES_INFO_EX - 動態 P-State 資訊
    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_DYNAMIC_PSTATES_INFO_EX
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public NV_GPU_UTILIZATION_DOMAIN[] Utilization;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_UTILIZATION_DOMAIN
    {
        public uint Present;   // 是否有效
        public uint Percentage; // 使用率百分比
    }

    // NV_GPU_POWER_STATUS - 電源狀態 (嘗試)
    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_POWER_STATUS_V1
    {
        public uint Version;
        public uint Flags;
        public uint Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public NV_GPU_POWER_STATUS_ENTRY[] Entries;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_POWER_STATUS_ENTRY
    {
        public uint Domain;
        public uint PowerTarget;     // 目標功耗 mW
        public uint PowerAvg;        // 平均功耗 mW
        public uint PowerMax;        // 最大功耗 mW
    }

    // 簡化的電壓查詢結構 - 使用 byte 陣列避免結構版本問題
    [StructLayout(LayoutKind.Sequential)]
    public struct NV_GPU_VOLTAGE_STATUS_RAW
    {
        public uint Version;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] Data;
    }

    // --- P/Invoke 宣告 ---

    [DllImport(NvApiDll, EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint interfaceId);

    // --- NVML P/Invoke for GPU Core Voltage ---
    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit_v2();

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetFieldValues(IntPtr device, int valuesCount, ref NvmlFieldValue_t values);

    // NVML Field Value struct (same as in NvmlWindowsProvider)
    [StructLayout(LayoutKind.Explicit)]
    private struct NvmlValue_t
    {
        [FieldOffset(0)] public double DoubleValue;
        [FieldOffset(0)] public uint UiValue;
        [FieldOffset(0)] public long SllValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlFieldValue_t
    {
        public uint FieldId;
        public uint ScopeId;
        public long Timestamp;
        public long LatencyUsec;
        public uint ValueType;
        public uint NvmlReturn;
        public NvmlValue_t Value;
    }

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
    private delegate int NvAPI_GPU_GetPstates20Delegate(
        IntPtr hPhysicalGpu,
        IntPtr pstatesInfo);  // 使用 IntPtr 避免結構版本問題

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetVoltageDomainsStatusDelegate(
        IntPtr hPhysicalGpu,
        IntPtr voltageStatus);  // 使用 IntPtr 避免結構版本問題

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetCurrentPstateDelegate(
        IntPtr hPhysicalGpu,
        out int currentPstate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetDynamicPstatesInfoExDelegate(
        IntPtr hPhysicalGpu,
        ref NV_GPU_DYNAMIC_PSTATES_INFO_EX pstatesInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GenericDelegate(
        IntPtr hPhysicalGpu,
        IntPtr data);

    // 函數指標
    private NvAPI_InitializeDelegate? _nvApiInitialize;
    private NvAPI_UnloadDelegate? _nvApiUnload;
    private NvAPI_EnumPhysicalGPUsDelegate? _nvApiEnumPhysicalGPUs;
    private NvAPI_GPU_GetFullNameDelegate? _nvApiGetFullName;
    private NvAPI_GPU_GetThermalSettingsDelegate? _nvApiGetThermalSettings;
    private NvAPI_GPU_GetPstates20Delegate? _nvApiGetPstates20;
    private NvAPI_GPU_GetVoltageDomainsStatusDelegate? _nvApiGetVoltageDomainsStatus;
    private NvAPI_GPU_GetCurrentPstateDelegate? _nvApiGetCurrentPstate;
    private NvAPI_GPU_GetDynamicPstatesInfoExDelegate? _nvApiGetDynamicPstatesInfoEx;
    private NvAPI_GPU_GenericDelegate? _nvApiGetVoltages;
    private NvAPI_GPU_GenericDelegate? _nvApiGetPowerSensors;
    private NvAPI_GPU_GenericDelegate? _nvApiGetCurrentVoltage;
    private NvAPI_GPU_GenericDelegate? _nvApiGetVoltageStatus;
    private NvAPI_GPU_GenericDelegate? _nvApiGetPowerMonitorStatus;
    private NvAPI_GPU_GenericDelegate? _nvApiGetPowerTopologyStatus;
    private NvAPI_GPU_GenericDelegate? _nvApiGetRailVoltage;
    private NvAPI_GPU_GenericDelegate? _nvApiGetPerfSensors;

    // 儲存成功讀取電壓的方法
    private bool _canReadVoltage;
    private string _voltageMethod = "None";

    // 儲存 PowerSensor 的索引（用於 16-pin 電壓）
    private int _powerSensor16PinIndex = -1;
    private int _powerSensorStructSize = 0;
    private int _powerSensorVersion = 0;

    public bool IsAvailable => _isInitialized;
    public bool CanReadVoltage => _canReadVoltage;
    public bool IsEstimatedVoltage => !_canReadVoltage; // 無法讀取真實電壓時為估算模式

    public NvApiWindowsProvider()
    {
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Log.Information("[NvAPI] 開始初始化 NvAPI...");

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
            Log.Information("[NvAPI] 找到 {Count} 個 GPU，使用第一個", gpuCount);

            // 取得 GPU 名稱
            var nameBuilder = new StringBuilder(NV_GPU_SHORT_STRING_LENGTH);
            result = _nvApiGetFullName!(_gpuHandle, nameBuilder);
            if (result == NVAPI_OK)
            {
                _gpuName = nameBuilder.ToString();
            }

            _isInitialized = true;

            // 測試電壓讀取
            TestVoltageReading();

            // 初始化 NVML 用於讀取 GPU Core Voltage (Field 169)
            InitializeNvmlForCoreVoltage();

            Log.Information("[NvAPI] 初始化完成 | GPU: {GpuName}", _gpuName);
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
            var ptrGetPstates20 = NvAPI_QueryInterface(NvAPI_GPU_GetPstates20_ID);
            var ptrGetVoltage = NvAPI_QueryInterface(NvAPI_GPU_GetVoltageDomainsStatus_ID);
            var ptrGetCurrentPstate = NvAPI_QueryInterface(NvAPI_GPU_GetCurrentPstate_ID);
            var ptrGetDynamicPstates = NvAPI_QueryInterface(NvAPI_GPU_GetDynamicPstatesInfoEx_ID);
            var ptrGetVoltages = NvAPI_QueryInterface(NvAPI_GPU_GetVoltages_ID);
            var ptrGetPowerSensors = NvAPI_QueryInterface(NvAPI_GPU_GetPowerSensors_ID);
            var ptrGetCurrentVoltage = NvAPI_QueryInterface(NvAPI_GPU_GetCurrentVoltage_ID);
            var ptrGetVoltageStatus = NvAPI_QueryInterface(NvAPI_GPU_GetVoltageStatus_ID);
            var ptrGetPowerMonitorStatus = NvAPI_QueryInterface(NvAPI_GPU_GetPowerMonitorStatus_ID);
            var ptrGetPowerTopologyStatus = NvAPI_QueryInterface(NvAPI_GPU_GetPowerTopologyStatus_ID);
            var ptrGetRailVoltage = NvAPI_QueryInterface(NvAPI_GPU_GetRailVoltage_ID);
            var ptrGetPerfSensors = NvAPI_QueryInterface(NvAPI_GPU_GetPerfSensors_ID);

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
            _nvApiGetPstates20 = ptrGetPstates20 != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetPstates20Delegate>(ptrGetPstates20)
                : null;
            _nvApiGetVoltageDomainsStatus = ptrGetVoltage != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetVoltageDomainsStatusDelegate>(ptrGetVoltage)
                : null;
            _nvApiGetCurrentPstate = ptrGetCurrentPstate != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetCurrentPstateDelegate>(ptrGetCurrentPstate)
                : null;
            _nvApiGetDynamicPstatesInfoEx = ptrGetDynamicPstates != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetDynamicPstatesInfoExDelegate>(ptrGetDynamicPstates)
                : null;
            _nvApiGetVoltages = ptrGetVoltages != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetVoltages)
                : null;
            _nvApiGetPowerSensors = ptrGetPowerSensors != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetPowerSensors)
                : null;
            _nvApiGetCurrentVoltage = ptrGetCurrentVoltage != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetCurrentVoltage)
                : null;
            _nvApiGetVoltageStatus = ptrGetVoltageStatus != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetVoltageStatus)
                : null;
            _nvApiGetPowerMonitorStatus = ptrGetPowerMonitorStatus != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetPowerMonitorStatus)
                : null;
            _nvApiGetPowerTopologyStatus = ptrGetPowerTopologyStatus != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetPowerTopologyStatus)
                : null;
            _nvApiGetRailVoltage = ptrGetRailVoltage != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetRailVoltage)
                : null;
            _nvApiGetPerfSensors = ptrGetPerfSensors != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GenericDelegate>(ptrGetPerfSensors)
                : null;

            Log.Information("[NvAPI] 介面查詢結果:");
            Log.Information("[NvAPI]   Initialize={0}, EnumGPUs={1}, GetFullName={2}",
                ptrInitialize != IntPtr.Zero, ptrEnumGPUs != IntPtr.Zero, ptrGetFullName != IntPtr.Zero);
            Log.Information("[NvAPI]   GetThermal={0}, GetPstates20={1}, GetVoltage={2}",
                ptrGetThermal != IntPtr.Zero, ptrGetPstates20 != IntPtr.Zero, ptrGetVoltage != IntPtr.Zero);
            Log.Information("[NvAPI]   GetCurrentPstate={0}, GetDynamicPstates={1}, GetVoltages={2}",
                ptrGetCurrentPstate != IntPtr.Zero, ptrGetDynamicPstates != IntPtr.Zero, ptrGetVoltages != IntPtr.Zero);
            Log.Information("[NvAPI]   GetPowerSensors={0}, GetCurrentVoltage={1}, GetVoltageStatus={2}",
                ptrGetPowerSensors != IntPtr.Zero, ptrGetCurrentVoltage != IntPtr.Zero, ptrGetVoltageStatus != IntPtr.Zero);
            Log.Information("[NvAPI]   GetPowerMonitor={0}, GetPowerTopology={1}, GetRailVoltage={2}, GetPerfSensors={3}",
                ptrGetPowerMonitorStatus != IntPtr.Zero, ptrGetPowerTopologyStatus != IntPtr.Zero,
                ptrGetRailVoltage != IntPtr.Zero, ptrGetPerfSensors != IntPtr.Zero);

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NvAPI] 查詢介面時發生錯誤");
            return false;
        }
    }

    private void TestVoltageReading()
    {
        Log.Information("[NvAPI] === 開始電壓讀取測試 ===");

        // 測試 1: GetCurrentPstate
        if (_nvApiGetCurrentPstate != null)
        {
            try
            {
                var result = _nvApiGetCurrentPstate(_gpuHandle, out int currentPstate);
                Log.Information("[NvAPI] GetCurrentPstate: Result={Result}, Pstate={Pstate}", result, currentPstate);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NvAPI] GetCurrentPstate 測試失敗");
            }
        }

        // 測試 2: GetDynamicPstatesInfoEx
        if (_nvApiGetDynamicPstatesInfoEx != null)
        {
            try
            {
                var pstatesInfo = new NV_GPU_DYNAMIC_PSTATES_INFO_EX
                {
                    Version = (uint)(Marshal.SizeOf<NV_GPU_DYNAMIC_PSTATES_INFO_EX>() | (1 << 16)),
                    Utilization = new NV_GPU_UTILIZATION_DOMAIN[8]
                };

                var result = _nvApiGetDynamicPstatesInfoEx(_gpuHandle, ref pstatesInfo);
                Log.Information("[NvAPI] GetDynamicPstatesInfoEx: Result={Result}, Flags={Flags}", result, pstatesInfo.Flags);

                if (result == NVAPI_OK)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (pstatesInfo.Utilization[i].Present != 0)
                        {
                            Log.Information("[NvAPI]   Domain {Id}: {Percent}%", i, pstatesInfo.Utilization[i].Percentage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NvAPI] GetDynamicPstatesInfoEx 測試失敗");
            }
        }

        // 測試 3: GetPstates20 - 嘗試多種結構版本
        if (_nvApiGetPstates20 != null)
        {
            TryGetPstates20WithDifferentVersions();
        }

        // 測試 4: GetVoltageDomainsStatus - 嘗試多種結構版本
        if (_nvApiGetVoltageDomainsStatus != null)
        {
            TryGetVoltageDomainsStatusWithDifferentVersions();
        }

        // 測試 5: GetPowerSensors (最佳的 16-pin 讀取方式)
        if (_nvApiGetPowerSensors != null)
        {
            TryGetPowerSensors();
        }

        // 測試 6: GetVoltages (未文檔化 API)
        if (_nvApiGetVoltages != null && !_canReadVoltage)
        {
            TryGetVoltagesUndocumented();
        }

        // 測試 7-12: 額外的可能 API
        if (!_canReadVoltage)
        {
            TryAdditionalVoltageApis();
        }

        Log.Information("[NvAPI] === 電壓讀取測試完成 | 可讀取電壓: {CanRead} | 方法: {Method} ===",
            _canReadVoltage, _voltageMethod);
    }

    /// <summary>
    /// 計算 MAKE_NVAPI_VERSION - 根據官方定義
    /// MAKE_NVAPI_VERSION(typeName, ver) = sizeof(typeName) | ((ver)<<16)
    /// </summary>
    private static uint MakeNvApiVersion(int structSize, int version)
    {
        return (uint)(structSize | (version << 16));
    }

    private void TryGetPstates20WithDifferentVersions()
    {
        // 根據 NVAPI R590 官方文件計算結構大小
        // NV_GPU_PSTATE20_CLOCK_ENTRY_V1: 28 bytes
        // NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY_V1: 24 bytes
        //
        // NV_GPU_PERF_PSTATES20_INFO_V2 估算:
        // header (version + flags + numPstates + numClocks + numBaseVoltages) = 20 bytes
        // pstates[16] × (pstateId + flags + clocks[8] + baseVoltages[4])
        //   = 16 × (4 + 4 + 28×8 + 24×4) = 16 × (8 + 224 + 96) = 16 × 328 = 5248 bytes
        // ov (numVoltages + voltages[4]) = 4 + 24×4 = 100 bytes
        // Total ≈ 5368 bytes (0x14F8)

        // 嘗試不同的結構大小 (包含官方文件推算值和實測常見值)
        int[] sizes = {
            0x14F8,  // 官方文件推算 (5368)
            0x10D8,  // 常見值 (4312)
            0x1440,  // 常見值 (5184)
            0x1500,  // 常見值 (5376)
            0x1580,  // Blackwell 可能用的較大版本
            0x0F20,  // 較舊版本
            0x1800,  // 較大緩衝區
        };

        // 版本號: 根據官方文件 VER1=1, VER2=2, VER3=3 (當前版本)
        int[] versions = { 3, 2, 1 };

        foreach (var ver in versions)
        {
            foreach (var size in sizes)
            {
                try
                {
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        // 清零
                        unsafe
                        {
                            byte* p = (byte*)ptr;
                            for (int i = 0; i < size; i++)
                                p[i] = 0;
                        }

                        // 設定版本 (MAKE_NVAPI_VERSION 格式)
                        uint nvVersion = MakeNvApiVersion(size, ver);
                        Marshal.WriteInt32(ptr, (int)nvVersion);

                        var result = _nvApiGetPstates20!(_gpuHandle, ptr);

                        if (result == NVAPI_OK)
                        {
                            Log.Information("[NvAPI] GetPstates20 成功! Size=0x{Size:X} ({SizeDec}), Ver={Ver}",
                                size, size, ver);

                            // 解析 header
                            uint flags = (uint)Marshal.ReadInt32(ptr, 4);
                            uint numPstates = (uint)Marshal.ReadInt32(ptr, 8);
                            uint numClocks = (uint)Marshal.ReadInt32(ptr, 12);
                            uint numBaseVoltages = (uint)Marshal.ReadInt32(ptr, 16);

                            Log.Information("[NvAPI]   Flags=0x{Flags:X}, NumPstates={NumPstates}, NumClocks={NumClocks}, NumBaseVoltages={NumVoltages}",
                                flags, numPstates, numClocks, numBaseVoltages);

                            // 解析電壓數據
                            if (numBaseVoltages > 0)
                            {
                                ParsePstates20VoltageData(ptr, size, numPstates, numClocks, numBaseVoltages);
                            }

                            return;
                        }
                        else
                        {
                            // 記錄非版本錯誤的其他錯誤
                            if (result != -9) // -9 = INCOMPATIBLE_STRUCT_VERSION
                            {
                                Log.Debug("[NvAPI] GetPstates20 Size=0x{Size:X}, Ver={Ver}: Result={Result}",
                                    size, ver, result);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[NvAPI] GetPstates20 Size=0x{Size:X}, Ver={Ver} 例外", size, ver);
                }
            }
        }

        Log.Warning("[NvAPI] GetPstates20 所有版本嘗試失敗 (可能是 Blackwell/GB200 架構不支援)");
    }

    /// <summary>
    /// 解析 Pstates20 中的電壓數據
    /// </summary>
    private void ParsePstates20VoltageData(IntPtr ptr, int totalSize, uint numPstates, uint numClocks, uint numBaseVoltages)
    {
        try
        {
            // 結構佈局:
            // [0-3] version
            // [4-7] flags
            // [8-11] numPstates
            // [12-15] numClocks
            // [16-19] numBaseVoltages
            // [20+] pstates[numPstates] → 每個包含 clocks[numClocks] 和 baseVoltages[numBaseVoltages]

            int headerSize = 20;

            // 嘗試多種 per-pstate 結構大小估算
            int clockEntrySize = 28;  // NV_GPU_PSTATE20_CLOCK_ENTRY_V1
            int voltageEntrySize = 24; // NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY_V1

            // 每個 P-State: pstateId(4) + flags(4) + clocks + voltages
            int perPstateSize = 4 + 4 + (int)(numClocks * clockEntrySize) + (int)(numBaseVoltages * voltageEntrySize);

            Log.Information("[NvAPI] 嘗試解析電壓 (PerPstateSize={Size})", perPstateSize);

            // 解析每個 P-State 的電壓
            for (int i = 0; i < numPstates && i < 4; i++)  // 只檢查前幾個
            {
                int pstateOffset = headerSize + (i * perPstateSize);
                if (pstateOffset + perPstateSize > totalSize) break;

                int pstateId = Marshal.ReadInt32(ptr, pstateOffset);
                int pstateFlags = Marshal.ReadInt32(ptr, pstateOffset + 4);

                // 電壓數據在 clocks 之後
                int voltageOffset = pstateOffset + 8 + (int)(numClocks * clockEntrySize);

                for (int v = 0; v < numBaseVoltages && v < 4; v++)
                {
                    int vOffset = voltageOffset + (v * voltageEntrySize);
                    if (vOffset + voltageEntrySize > totalSize) break;

                    uint domainId = (uint)Marshal.ReadInt32(ptr, vOffset);
                    uint vFlags = (uint)Marshal.ReadInt32(ptr, vOffset + 4);
                    uint volt_uV = (uint)Marshal.ReadInt32(ptr, vOffset + 8);

                    if (volt_uV > 0 && volt_uV < 10000000) // 合理的電壓範圍 (< 10V)
                    {
                        float voltageV = volt_uV / 1000000.0f;
                        Log.Information("[NvAPI]   P{PstateId} Voltage[{Idx}]: Domain={Domain}, {VoltUV} uV = {VoltV:F4}V",
                            pstateId, v, domainId, volt_uV, voltageV);

                        if (!_canReadVoltage && voltageV > 0.3f && voltageV < 2.0f)  // GPU Core 電壓範圍
                        {
                            _lastVoltage = voltageV;
                            _canReadVoltage = true;
                            _voltageMethod = $"GetPstates20 (P{pstateId}, Domain={domainId})";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NvAPI] 解析 Pstates20 電壓數據時發生錯誤");
        }
    }

    private void TryGetVoltageDomainsStatusWithDifferentVersions()
    {
        // 嘗試不同的結構大小和版本
        int[] sizes = { 0x50, 0x88, 0x100, 0x150 };
        int[] versions = { 1, 2, 3 };

        foreach (var size in sizes)
        {
            foreach (var ver in versions)
            {
                try
                {
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        // 清零並設定版本
                        for (int i = 0; i < size; i++)
                            Marshal.WriteByte(ptr, i, 0);

                        uint version = (uint)(size | (ver << 16));
                        Marshal.WriteInt32(ptr, (int)version);

                        var result = _nvApiGetVoltageDomainsStatus!(_gpuHandle, ptr);

                        if (result == NVAPI_OK)
                        {
                            Log.Information("[NvAPI] GetVoltageDomainsStatus 成功! Size={Size}, Ver={Ver}", size, ver);

                            uint flags = (uint)Marshal.ReadInt32(ptr, 4);
                            uint count = (uint)Marshal.ReadInt32(ptr, 8);

                            Log.Information("[NvAPI]   Flags={Flags}, Count={Count}", flags, count);

                            // 解析電壓域
                            for (int i = 0; i < count && i < 16; i++)
                            {
                                int offset = 12 + i * 8;
                                uint domainId = (uint)Marshal.ReadInt32(ptr, offset);
                                uint voltage_uV = (uint)Marshal.ReadInt32(ptr, offset + 4);

                                if (voltage_uV > 0)
                                {
                                    Log.Information("[NvAPI]   Domain {Id}: {Voltage} uV ({VoltageV:F3} V)",
                                        domainId, voltage_uV, voltage_uV / 1000000.0f);

                                    _canReadVoltage = true;
                                    _voltageMethod = $"GetVoltageDomainsStatus (Size={size}, Ver={ver})";
                                }
                            }

                            if (_canReadVoltage) return;
                        }
                        else if (result != -9 && result != -104)
                        {
                            Log.Debug("[NvAPI] GetVoltageDomainsStatus Size={Size}, Ver={Ver}: Result={Result}", size, ver, result);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                catch { }
            }
        }

        Log.Warning("[NvAPI] GetVoltageDomainsStatus 所有版本嘗試失敗");
    }

    /// <summary>
    /// 嘗試使用 NvAPI_GPU_GetPowerSensors 讀取電源感測器
    /// 這是讀取 16-pin (12VHPWR) 電壓最穩定的方法
    /// </summary>
    private void TryGetPowerSensors()
    {
        Log.Information("[NvAPI] === 開始 GetPowerSensors 探測 ===");

        // NV_GPU_POWER_SENSORS 結構估計大小範圍
        // 常見大小: 0x100, 0x200, 0x300, 0x400, 0x500, 0x800
        int[] sizes = { 0x100, 0x180, 0x200, 0x280, 0x300, 0x400, 0x500, 0x600, 0x800, 0x1000 };
        int[] versions = { 1, 2, 3 };

        foreach (var size in sizes)
        {
            foreach (var ver in versions)
            {
                try
                {
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        // 清零並設定版本
                        for (int i = 0; i < size; i++)
                            Marshal.WriteByte(ptr, i, 0);

                        uint version = (uint)(size | (ver << 16));
                        Marshal.WriteInt32(ptr, (int)version);

                        var result = _nvApiGetPowerSensors!(_gpuHandle, ptr);

                        if (result == NVAPI_OK)
                        {
                            Log.Information("[NvAPI] GetPowerSensors 成功! Size=0x{Size:X}, Ver={Ver}", size, ver);

                            _powerSensorStructSize = size;
                            _powerSensorVersion = ver;

                            // 解析結構
                            ParsePowerSensorsData(ptr, size);
                            return;
                        }
                        else if (result != -9 && result != -104) // 不是版本錯誤或不支援
                        {
                            Log.Debug("[NvAPI] GetPowerSensors Size=0x{Size:X}, Ver={Ver}: Result={Result}", size, ver, result);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[NvAPI] GetPowerSensors Size=0x{Size:X}, Ver={Ver} 例外", size, ver);
                }
            }
        }

        Log.Warning("[NvAPI] GetPowerSensors 所有版本嘗試失敗");
    }

    /// <summary>
    /// 解析 PowerSensors 數據並輸出診斷資訊
    /// </summary>
    private void ParsePowerSensorsData(IntPtr ptr, int size)
    {
        try
        {
            // 輸出前 64 bytes 的原始數據供分析
            Log.Information("[NvAPI] PowerSensors 原始數據 (前 64 bytes):");
            var headerBytes = new byte[Math.Min(64, size)];
            Marshal.Copy(ptr, headerBytes, 0, headerBytes.Length);
            Log.Information("[NvAPI]   {Data}", BitConverter.ToString(headerBytes));

            // 嘗試解析標準結構
            // 假設結構: Version(4) + Flags(4) + Count(4) + Reserved(4) + Sensors[...]
            uint flags = (uint)Marshal.ReadInt32(ptr, 4);
            uint count = (uint)Marshal.ReadInt32(ptr, 8);

            Log.Information("[NvAPI] PowerSensors: Flags=0x{Flags:X}, Count={Count}", flags, count);

            if (count > 0 && count < 32) // 合理的感測器數量
            {
                // 嘗試多種感測器結構大小
                int[] sensorSizes = { 0x18, 0x20, 0x24, 0x28, 0x30, 0x40 };

                foreach (var sensorSize in sensorSizes)
                {
                    Log.Information("[NvAPI] 嘗試 Sensor 結構大小: 0x{SensorSize:X}", sensorSize);

                    bool found16Pin = false;
                    for (int i = 0; i < count && i < 32; i++)
                    {
                        int offset = 16 + (i * sensorSize); // 假設 header 為 16 bytes
                        if (offset + sensorSize > size) break;

                        // 讀取可能的感測器數據
                        // 假設結構: Domain(4) + Unknown(4) + Current_mA(4) + Voltage_mV(4) + Power_mW(4) + ...
                        uint domain = (uint)Marshal.ReadInt32(ptr, offset);
                        uint field1 = (uint)Marshal.ReadInt32(ptr, offset + 4);
                        uint field2 = (uint)Marshal.ReadInt32(ptr, offset + 8);
                        uint field3 = (uint)Marshal.ReadInt32(ptr, offset + 12);
                        uint field4 = (uint)Marshal.ReadInt32(ptr, offset + 16);

                        // 檢查是否有合理的電壓值 (1000-20000 mV = 1V-20V)
                        bool hasVoltage = (field1 >= 1000 && field1 <= 20000) ||
                                         (field2 >= 1000 && field2 <= 20000) ||
                                         (field3 >= 1000 && field3 <= 20000) ||
                                         (field4 >= 1000 && field4 <= 20000);

                        // 檢查是否有合理的 16-pin 電壓值 (10000-14000 mV = 10V-14V)
                        bool has16PinVoltage = (field1 >= 10000 && field1 <= 14000) ||
                                               (field2 >= 10000 && field2 <= 14000) ||
                                               (field3 >= 10000 && field3 <= 14000) ||
                                               (field4 >= 10000 && field4 <= 14000);

                        if (hasVoltage || domain < 100)
                        {
                            Log.Information("[NvAPI]   Sensor[{Index}]: Domain={Domain}, F1={F1}, F2={F2}, F3={F3}, F4={F4}",
                                i, domain, field1, field2, field3, field4);

                            // 判斷是否為 16-pin 感測器
                            if (has16PinVoltage)
                            {
                                found16Pin = true;
                                _powerSensor16PinIndex = i;

                                // 確定電壓欄位
                                float voltage_V = 0;
                                if (field1 >= 10000 && field1 <= 14000)
                                    voltage_V = field1 / 1000.0f;
                                else if (field2 >= 10000 && field2 <= 14000)
                                    voltage_V = field2 / 1000.0f;
                                else if (field3 >= 10000 && field3 <= 14000)
                                    voltage_V = field3 / 1000.0f;
                                else if (field4 >= 10000 && field4 <= 14000)
                                    voltage_V = field4 / 1000.0f;

                                Log.Information("[NvAPI]   >>> 找到可能的 16-pin 感測器! Index={Index}, 電壓={Voltage:F3}V",
                                    i, voltage_V);

                                _lastVoltage = voltage_V;
                                _canReadVoltage = true;
                                _voltageMethod = $"GetPowerSensors (Sensor[{i}], Size=0x{_powerSensorStructSize:X})";
                            }
                        }
                    }

                    if (found16Pin) return;
                }
            }

            // 如果標準解析失敗，進行深度掃描
            Log.Information("[NvAPI] 標準解析未找到電壓，進行深度掃描...");
            DeepScanPowerSensorsData(ptr, size);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NvAPI] 解析 PowerSensors 數據時發生錯誤");
        }
    }

    /// <summary>
    /// 深度掃描 PowerSensors 數據，尋找可能的電壓值
    /// </summary>
    private void DeepScanPowerSensorsData(IntPtr ptr, int size)
    {
        Log.Information("[NvAPI] === PowerSensors 深度掃描 ===");

        // 掃描所有 4-byte 對齊的位置，尋找合理的電壓值
        var candidates = new List<(int offset, uint value, float voltage)>();

        for (int offset = 0; offset < size - 4; offset += 4)
        {
            uint value = (uint)Marshal.ReadInt32(ptr, offset);

            // mV 範圍: 10000-14000 (10V-14V for 16-pin)
            if (value >= 10000 && value <= 14000)
            {
                float voltage = value / 1000.0f;
                candidates.Add((offset, value, voltage));
                Log.Information("[NvAPI]   Offset 0x{Offset:X4}: {Value} mV = {Voltage:F3}V (可能是 16-pin)",
                    offset, value, voltage);
            }
            // uV 範圍: 10000000-14000000 (10V-14V)
            else if (value >= 10000000 && value <= 14000000)
            {
                float voltage = value / 1000000.0f;
                candidates.Add((offset, value, voltage));
                Log.Information("[NvAPI]   Offset 0x{Offset:X4}: {Value} uV = {Voltage:F3}V (可能是 16-pin)",
                    offset, value, voltage);
            }
            // GPU Core 電壓範圍: 500-2000 mV (0.5V-2V)
            else if (value >= 500 && value <= 2000)
            {
                float voltage = value / 1000.0f;
                Log.Information("[NvAPI]   Offset 0x{Offset:X4}: {Value} mV = {Voltage:F3}V (可能是 GPU Core)",
                    offset, value, voltage);
            }
        }

        if (candidates.Count > 0)
        {
            // 使用第一個 16-pin 範圍的候選值
            var best = candidates[0];
            _lastVoltage = best.voltage;
            _canReadVoltage = true;
            _voltageMethod = $"GetPowerSensors (DeepScan Offset=0x{best.offset:X})";
            Log.Information("[NvAPI] 深度掃描找到電壓! Offset=0x{Offset:X}, Value={Value}, Voltage={Voltage:F3}V",
                best.offset, best.value, best.voltage);
        }
    }

    private void TryGetVoltagesUndocumented()
    {
        // 嘗試未文檔化的 GetVoltages API
        int[] sizes = { 0x100, 0x200, 0x300 };

        foreach (var size in sizes)
        {
            try
            {
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++)
                        Marshal.WriteByte(ptr, i, 0);

                    // 嘗試不同的版本號
                    for (int ver = 1; ver <= 3; ver++)
                    {
                        uint version = (uint)(size | (ver << 16));
                        Marshal.WriteInt32(ptr, (int)version);

                        var result = _nvApiGetVoltages!(_gpuHandle, ptr);

                        if (result == NVAPI_OK)
                        {
                            Log.Information("[NvAPI] GetVoltages (undoc) 成功! Size={Size}, Ver={Ver}", size, ver);

                            // 輸出前 32 bytes 供分析
                            var bytes = new byte[Math.Min(32, size)];
                            Marshal.Copy(ptr, bytes, 0, bytes.Length);
                            Log.Information("[NvAPI]   Data: {Data}", BitConverter.ToString(bytes));

                            return;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 嘗試額外的可能電壓 API
    /// </summary>
    private void TryAdditionalVoltageApis()
    {
        Log.Information("[NvAPI] === 嘗試額外電壓/電源 API ===");

        // 優先深度探測 GetCurrentVoltage (函數指標存在於 Blackwell)
        if (_nvApiGetCurrentVoltage != null)
        {
            TryGetCurrentVoltageDeep();
            if (_canReadVoltage) return;
        }

        // 通用測試函數
        void TryGenericApi(NvAPI_GPU_GenericDelegate? apiFunc, string apiName, uint apiId)
        {
            if (apiFunc == null)
            {
                Log.Debug("[NvAPI] {ApiName} (0x{ApiId:X8}) 不可用", apiName, apiId);
                return;
            }

            int[] sizes = { 0x10, 0x20, 0x40, 0x80, 0x100, 0x180, 0x200, 0x300 };
            int[] versions = { 1, 2, 3, 4 };

            foreach (var size in sizes)
            {
                foreach (var ver in versions)
                {
                    try
                    {
                        IntPtr ptr = Marshal.AllocHGlobal(size);
                        try
                        {
                            for (int i = 0; i < size; i++)
                                Marshal.WriteByte(ptr, i, 0);

                            uint version = (uint)(size | (ver << 16));
                            Marshal.WriteInt32(ptr, (int)version);

                            var result = apiFunc(_gpuHandle, ptr);

                            if (result == NVAPI_OK)
                            {
                                Log.Information("[NvAPI] {ApiName} 成功! Size=0x{Size:X}, Ver={Ver}", apiName, size, ver);

                                // 輸出完整數據
                                var bytes = new byte[size];
                                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                                Log.Information("[NvAPI]   Data: {Data}", BitConverter.ToString(bytes));

                                // 掃描可能的電壓值
                                ScanForVoltageValues(ptr, size, apiName);
                                return;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(ptr);
                        }
                    }
                    catch { }
                }
            }

            Log.Debug("[NvAPI] {ApiName} 所有嘗試失敗", apiName);
        }

        // 測試其他 API
        TryGenericApi(_nvApiGetVoltageStatus, "GetVoltageStatus", NvAPI_GPU_GetVoltageStatus_ID);
        TryGenericApi(_nvApiGetPowerMonitorStatus, "GetPowerMonitorStatus", NvAPI_GPU_GetPowerMonitorStatus_ID);
        TryGenericApi(_nvApiGetPowerTopologyStatus, "GetPowerTopologyStatus", NvAPI_GPU_GetPowerTopologyStatus_ID);
        TryGenericApi(_nvApiGetRailVoltage, "GetRailVoltage", NvAPI_GPU_GetRailVoltage_ID);
        TryGenericApi(_nvApiGetPerfSensors, "GetPerfSensors", NvAPI_GPU_GetPerfSensors_ID);
    }

    /// <summary>
    /// 深度探測 GetCurrentVoltage API (0x465F9BCF)
    /// 這個 API 在 Blackwell 上有函數指標，需要找到正確的結構格式
    /// </summary>
    private void TryGetCurrentVoltageDeep()
    {
        Log.Information("[NvAPI] === 深度探測 GetCurrentVoltage (0x465F9BCF) ===");

        // 追蹤已見過的錯誤碼以減少日誌
        var seenErrors = new HashSet<int>();

        // 嘗試更細緻的結構大小組合
        int[] sizes = {
            0x08, 0x0C, 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30,
            0x34, 0x38, 0x3C, 0x40, 0x48, 0x50, 0x60, 0x70, 0x80, 0xA0, 0xC0
        };
        int[] versions = { 1, 2, 3, 4, 5 };

        foreach (var ver in versions)
        {
            foreach (var size in sizes)
            {
                try
                {
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        // 清零
                        for (int i = 0; i < size; i++)
                            Marshal.WriteByte(ptr, i, 0);

                        // 設定版本
                        uint nvVersion = MakeNvApiVersion(size, ver);
                        Marshal.WriteInt32(ptr, (int)nvVersion);

                        var result = _nvApiGetCurrentVoltage!(_gpuHandle, ptr);

                        if (result == NVAPI_OK)
                        {
                            Log.Information("[NvAPI] GetCurrentVoltage 成功! Size=0x{Size:X}, Ver={Ver}", size, ver);

                            // 輸出完整數據
                            var bytes = new byte[size];
                            Marshal.Copy(ptr, bytes, 0, size);
                            Log.Information("[NvAPI]   RawData: {Data}", BitConverter.ToString(bytes));

                            // 解析數據 - 跳過 version 欄位
                            for (int offset = 4; offset < size - 3; offset += 4)
                            {
                                uint value = (uint)Marshal.ReadInt32(ptr, offset);

                                // 檢查各種可能的電壓格式
                                // 1. GPU Core 微伏 (uV): 500000-2000000 (0.5V-2V)
                                if (value >= 500000 && value <= 2000000)
                                {
                                    float voltageV = value / 1000000.0f;
                                    Log.Information("[NvAPI]   Offset 0x{Offset:X}: {Value} uV = {Voltage:F4}V (GPU Core)",
                                        offset, value, voltageV);

                                    if (!_canReadVoltage)
                                    {
                                        _lastVoltage = voltageV;
                                        _canReadVoltage = true;
                                        _voltageMethod = $"GetCurrentVoltage (V{ver}, 0x{size:X}, +0x{offset:X}, uV)";
                                    }
                                }
                                // 2. GPU Core 毫伏 (mV): 500-2000
                                else if (value >= 500 && value <= 2000)
                                {
                                    float voltageV = value / 1000.0f;
                                    Log.Information("[NvAPI]   Offset 0x{Offset:X}: {Value} mV = {Voltage:F3}V (GPU Core mV)",
                                        offset, value, voltageV);
                                }
                                // 3. 16-pin 微伏 (uV): 10000000-14000000 (10V-14V)
                                else if (value >= 10000000 && value <= 14000000)
                                {
                                    float voltageV = value / 1000000.0f;
                                    Log.Information("[NvAPI]   Offset 0x{Offset:X}: {Value} uV = {Voltage:F4}V (16-pin)",
                                        offset, value, voltageV);

                                    _lastVoltage = voltageV;
                                    _canReadVoltage = true;
                                    _voltageMethod = $"GetCurrentVoltage (V{ver}, 0x{size:X}, +0x{offset:X}, 16-pin uV)";
                                }
                                // 4. 16-pin 毫伏 (mV): 10000-14000
                                else if (value >= 10000 && value <= 14000)
                                {
                                    float voltageV = value / 1000.0f;
                                    Log.Information("[NvAPI]   Offset 0x{Offset:X}: {Value} mV = {Voltage:F3}V (16-pin mV)",
                                        offset, value, voltageV);

                                    _lastVoltage = voltageV;
                                    _canReadVoltage = true;
                                    _voltageMethod = $"GetCurrentVoltage (V{ver}, 0x{size:X}, +0x{offset:X}, 16-pin mV)";
                                }
                                // 5. 非零有效值 (記錄)
                                else if (value > 0 && value < 0x01000000)
                                {
                                    Log.Debug("[NvAPI]   Offset 0x{Offset:X}: {Value} (0x{ValueHex:X8})",
                                        offset, value, value);
                                }
                            }

                            if (_canReadVoltage) return;
                        }
                        else
                        {
                            // 追蹤唯一錯誤碼
                            if (!seenErrors.Contains(result))
                            {
                                seenErrors.Add(result);
                                Log.Debug("[NvAPI] GetCurrentVoltage 新錯誤碼: {Result} (0x{ResultHex:X})", result, (uint)result);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                catch { }
            }
        }

        // 輸出所有遇到的錯誤碼
        if (seenErrors.Count > 0)
        {
            Log.Information("[NvAPI] GetCurrentVoltage 遇到的錯誤碼: {Errors}",
                string.Join(", ", seenErrors.Select(e => $"{e} (0x{(uint)e:X})")));
        }

        Log.Warning("[NvAPI] GetCurrentVoltage 所有版本/大小組合嘗試失敗");
        Log.Warning("[NvAPI] 注意: RTX 50 系列 (Blackwell/GB200) 可能不支援通過 NvAPI 讀取 16-pin 電壓");
    }

    /// <summary>
    /// 掃描記憶體區塊中的可能電壓值
    /// </summary>
    private void ScanForVoltageValues(IntPtr ptr, int size, string source)
    {
        for (int offset = 4; offset < size - 4; offset += 4)
        {
            uint value = (uint)Marshal.ReadInt32(ptr, offset);

            // 16-pin 電壓 mV 範圍 (10V-14V)
            if (value >= 10000 && value <= 14000)
            {
                float voltage = value / 1000.0f;
                Log.Information("[NvAPI] {Source} 找到可能的 16-pin 電壓: Offset=0x{Offset:X}, {Value} mV = {Voltage:F3}V",
                    source, offset, value, voltage);

                if (!_canReadVoltage)
                {
                    _lastVoltage = voltage;
                    _canReadVoltage = true;
                    _voltageMethod = $"{source} (mV at 0x{offset:X})";
                }
            }
            // 16-pin 電壓 uV 範圍 (10V-14V)
            else if (value >= 10000000 && value <= 14000000)
            {
                float voltage = value / 1000000.0f;
                Log.Information("[NvAPI] {Source} 找到可能的 16-pin 電壓: Offset=0x{Offset:X}, {Value} uV = {Voltage:F3}V",
                    source, offset, value, voltage);

                if (!_canReadVoltage)
                {
                    _lastVoltage = voltage;
                    _canReadVoltage = true;
                    _voltageMethod = $"{source} (uV at 0x{offset:X})";
                }
            }
            // GPU Core 電壓 mV 範圍 (0.5V-2V)
            else if (value >= 500 && value <= 2000)
            {
                Log.Information("[NvAPI] {Source} 找到可能的 GPU Core 電壓: Offset=0x{Offset:X}, {Value} mV = {Voltage:F3}V",
                    source, offset, value, value / 1000.0f);
            }
        }
    }

    public string GetGpuName() => _isInitialized ? _gpuName : "N/A";

    public GpuReading GetCurrentReading()
    {
        if (!_isInitialized)
            return new GpuReading(0, 0, DateTime.Now);

        float temperature = GetTemperature();
        float voltage = GetVoltage();
        float? gpuCoreVoltage = GetGpuCoreVoltage();

        return new GpuReading(voltage, temperature, DateTime.Now, gpuCoreVoltage);
    }

    /// <summary>
    /// 初始化 NVML 用於讀取 GPU Core Voltage (Field 169)
    /// </summary>
    private void InitializeNvmlForCoreVoltage()
    {
        try
        {
            if (nvmlInit_v2() == 0)
            {
                if (nvmlDeviceGetHandleByIndex_v2(0, out _nvmlDeviceHandle) == 0)
                {
                    _nvmlInitialized = true;
                    Log.Information("[NvAPI] NVML 初始化成功，用於讀取 GPU Core Voltage");
                }
            }
        }
        catch (DllNotFoundException)
        {
            // NVML 不可用，忽略
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[NvAPI] NVML 初始化失敗");
        }
    }

    /// <summary>
    /// 讀取 GPU Core Voltage (NVML Field 169)
    /// RTX 50 系列可用，值為 mV 需轉換為 V
    /// </summary>
    private float? GetGpuCoreVoltage()
    {
        if (!_nvmlInitialized)
            return null;

        try
        {
            var field = new NvmlFieldValue_t { FieldId = NVML_FIELD_GPU_CORE_VOLTAGE };
            if (nvmlDeviceGetFieldValues(_nvmlDeviceHandle, 1, ref field) == 0 && field.NvmlReturn == 0)
            {
                // Field 169 回傳 mV (如 728 = 0.728V)
                float voltageInMv = field.Value.UiValue;
                if (voltageInMv > 0 && voltageInMv < 2000) // 合理範圍 0-2V
                {
                    return voltageInMv / 1000.0f; // 轉換為 V
                }
            }
        }
        catch
        {
            // 忽略錯誤
        }
        return null;
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

            var result = _nvApiGetThermalSettings(_gpuHandle, (int)NVAPI_THERMAL_TARGET_GPU, ref thermalSettings);
            if (result == NVAPI_OK && thermalSettings.Count > 0)
            {
                return thermalSettings.Sensor[0].CurrentTemp;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NvAPI] 讀取溫度失敗");
        }

        return 0;
    }

    private float GetVoltage()
    {
        // 如果已經測試成功，使用對應的方法讀取
        // 優先使用 PowerSensors (最適合 16-pin 電壓)
        if (_canReadVoltage && _voltageMethod.Contains("GetPowerSensors"))
        {
            var voltage = TryReadVoltageFromPowerSensors();
            if (voltage > 0)
            {
                _lastVoltage = voltage;
                return _lastVoltage;
            }
        }

        if (_canReadVoltage && _voltageMethod.Contains("GetVoltageDomainsStatus"))
        {
            var voltage = TryReadVoltageFromDomainsStatus();
            if (voltage > 0)
            {
                _lastVoltage = voltage;
                return _lastVoltage;
            }
        }

        if (_canReadVoltage && _voltageMethod.Contains("GetPstates20"))
        {
            var voltage = TryReadVoltageFromPstates20();
            if (voltage > 0)
            {
                _lastVoltage = voltage;
                return _lastVoltage;
            }
        }

        // 如果無法讀取，返回上次成功的值或估算值
        return _lastVoltage > 0 ? _lastVoltage : 12.0f;
    }

    private float TryReadVoltageFromDomainsStatus()
    {
        if (_nvApiGetVoltageDomainsStatus == null) return 0;

        try
        {
            // 使用測試時成功的大小和版本
            int size = 0x88;
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++)
                    Marshal.WriteByte(ptr, i, 0);

                uint version = (uint)(size | (1 << 16));
                Marshal.WriteInt32(ptr, (int)version);

                var result = _nvApiGetVoltageDomainsStatus(_gpuHandle, ptr);
                if (result == NVAPI_OK)
                {
                    uint count = (uint)Marshal.ReadInt32(ptr, 8);
                    uint maxVoltage = 0;

                    for (int i = 0; i < count && i < 16; i++)
                    {
                        int offset = 12 + i * 8;
                        uint voltage_uV = (uint)Marshal.ReadInt32(ptr, offset + 4);
                        if (voltage_uV > maxVoltage)
                            maxVoltage = voltage_uV;
                    }

                    if (maxVoltage > 0)
                        return maxVoltage / 1000000.0f;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { }

        return 0;
    }

    private float TryReadVoltageFromPstates20()
    {
        if (_nvApiGetPstates20 == null) return 0;

        try
        {
            // 使用測試時成功的大小和版本
            int size = 0x10D8;
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++)
                    Marshal.WriteByte(ptr, i, 0);

                uint version = (uint)(size | (2 << 16));
                Marshal.WriteInt32(ptr, (int)version);

                var result = _nvApiGetPstates20(_gpuHandle, ptr);
                if (result == NVAPI_OK)
                {
                    uint numBaseVoltages = (uint)Marshal.ReadInt32(ptr, 16);

                    // 電壓數據在結構的後半部分，偏移量取決於時鐘數據
                    // 簡化：掃描結構尋找合理的電壓值 (100000 - 2000000 uV = 0.1V - 2V)
                    for (int offset = 20; offset < size - 4; offset += 4)
                    {
                        uint value = (uint)Marshal.ReadInt32(ptr, offset);
                        if (value >= 500000 && value <= 2000000) // 0.5V - 2V range
                        {
                            return value / 1000000.0f;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// 從 PowerSensors 讀取 16-pin 電壓
    /// 使用探測時發現的結構大小和版本
    /// </summary>
    private float TryReadVoltageFromPowerSensors()
    {
        if (_nvApiGetPowerSensors == null || _powerSensorStructSize == 0) return 0;

        try
        {
            IntPtr ptr = Marshal.AllocHGlobal(_powerSensorStructSize);
            try
            {
                for (int i = 0; i < _powerSensorStructSize; i++)
                    Marshal.WriteByte(ptr, i, 0);

                uint version = (uint)(_powerSensorStructSize | (_powerSensorVersion << 16));
                Marshal.WriteInt32(ptr, (int)version);

                var result = _nvApiGetPowerSensors(_gpuHandle, ptr);
                if (result == NVAPI_OK)
                {
                    // 如果有已知的感測器索引，直接讀取
                    if (_powerSensor16PinIndex >= 0)
                    {
                        // 嘗試常見的感測器結構大小
                        int[] sensorSizes = { 0x18, 0x20, 0x24, 0x28, 0x30, 0x40 };
                        foreach (var sensorSize in sensorSizes)
                        {
                            int offset = 16 + (_powerSensor16PinIndex * sensorSize);
                            if (offset + 20 > _powerSensorStructSize) continue;

                            // 讀取可能的電壓欄位
                            for (int fieldOffset = 0; fieldOffset < 20; fieldOffset += 4)
                            {
                                uint value = (uint)Marshal.ReadInt32(ptr, offset + fieldOffset);
                                // 16-pin mV 範圍
                                if (value >= 10000 && value <= 14000)
                                    return value / 1000.0f;
                                // 16-pin uV 範圍
                                if (value >= 10000000 && value <= 14000000)
                                    return value / 1000000.0f;
                            }
                        }
                    }

                    // 如果索引法失敗，進行快速掃描
                    for (int offset = 16; offset < _powerSensorStructSize - 4; offset += 4)
                    {
                        uint value = (uint)Marshal.ReadInt32(ptr, offset);
                        // 16-pin mV 範圍
                        if (value >= 10000 && value <= 14000)
                            return value / 1000.0f;
                        // 16-pin uV 範圍
                        if (value >= 10000000 && value <= 14000000)
                            return value / 1000000.0f;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[NvAPI] TryReadVoltageFromPowerSensors 失敗");
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
