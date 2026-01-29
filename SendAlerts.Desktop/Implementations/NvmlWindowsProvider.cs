using System;
using System.Runtime.InteropServices;
using System.Text;
using SendAlerts.Core.Interfaces;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// Windows 平台 NVML GPU 資料提供者
/// 讀取 GPU 功耗和溫度
/// </summary>
public class NvmlWindowsProvider : IGpuProvider
{
    private const string NvmlDll = "nvml.dll";
    private IntPtr _deviceHandle;
    private bool _isInitialized;
    private string _gpuName = "Unknown";
    private float _powerLimit;

    // --- NVML P/Invoke 宣告 ---
    [DllImport(NvmlDll, EntryPoint = "nvmlInit_v2")]
    private static extern int nvmlInit();

    [DllImport(NvmlDll, EntryPoint = "nvmlShutdown")]
    private static extern int nvmlShutdown();

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, uint type, out uint temp);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetName")]
    private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerUsage")]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerManagementLimit")]
    private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint limit);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, ref NvmlUtilization_t utilization);

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlUtilization_t
    {
        public uint Gpu;      // GPU 核心使用率 (%)
        public uint Memory;   // 記憶體使用率 (%)
    }

    public bool IsAvailable => _isInitialized;
    public float PowerLimit => _powerLimit;

    // 硬體模式與動態標籤 (GPU 模式)
    public HardwareMode Mode => HardwareMode.Gpu;
    public string PrimaryMetricLabel => "GPU Utilization";
    public string TemperatureLabel => "GPU Temperature";
    public string TemperatureUnit => "°C";
    public string SecondaryMetricLabel => "Power Usage";
    public string SecondaryMetricUnit => "W";

    public NvmlWindowsProvider()
    {
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            var result = nvmlInit();
            if (result != 0)
            {
                Log.Error("[NVML] 初始化失敗，錯誤碼: {ErrorCode}", result);
                return;
            }

            result = nvmlDeviceGetHandleByIndex(0, out _deviceHandle);
            if (result != 0)
            {
                Log.Error("[NVML] 無法取得 GPU 裝置，錯誤碼: {ErrorCode}", result);
                nvmlShutdown();
                return;
            }

            // 讀取 GPU 名稱
            var nameBuilder = new StringBuilder(64);
            nvmlDeviceGetName(_deviceHandle, nameBuilder, (uint)nameBuilder.Capacity);
            _gpuName = nameBuilder.ToString();

            // 讀取功耗限制
            if (nvmlDeviceGetPowerManagementLimit(_deviceHandle, out uint limitMw) == 0)
            {
                _powerLimit = limitMw / 1000.0f;
            }

            _isInitialized = true;
            Log.Information("[NVML] 初始化完成 | GPU: {GpuName} | PowerLimit: {PowerLimit:F1}W", _gpuName, _powerLimit);
        }
        catch (DllNotFoundException ex)
        {
            Log.Error(ex, "[NVML] 找不到 nvml.dll，請確認已安裝 NVIDIA 驅動程式");
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NVML] 初始化時發生未預期的錯誤");
            _isInitialized = false;
        }
    }

    public string GetGpuName() => _isInitialized ? _gpuName : "N/A";

    public GpuReading GetCurrentReading()
    {
        if (!_isInitialized)
            return new GpuReading(0, 0, 0, DateTime.Now);

        // 讀取 GPU 使用率
        float gpuUtilization = 0;
        var utilization = new NvmlUtilization_t();
        if (nvmlDeviceGetUtilizationRates(_deviceHandle, ref utilization) == 0)
        {
            gpuUtilization = utilization.Gpu;
        }

        // 讀取溫度
        float temp = 0;
        if (nvmlDeviceGetTemperature(_deviceHandle, 0, out uint tempValue) == 0)
        {
            temp = tempValue;
        }

        // 讀取功耗
        float power = 0;
        if (nvmlDeviceGetPowerUsage(_deviceHandle, out uint powerMw) == 0)
        {
            power = powerMw / 1000.0f; // mW -> W
        }

        return new GpuReading(gpuUtilization, temp, power, DateTime.Now);
    }

    public void Dispose()
    {
        if (_isInitialized)
        {
            nvmlShutdown();
            _isInitialized = false;
            Log.Information("[NVML] 已安全關閉");
        }
    }
}
