using System;
using System.Runtime.InteropServices;
using System.Text;
using _16pin_vmon.Core.Interfaces;
using _16pin_vmon.Services;
using Serilog;

namespace _16pin_vmon.Desktop.Implementations;

/// <summary>
/// Windows 平台 NVML GPU 資料提供者
/// 實作三段式 Field ID 偵測邏輯 (T2-3)
/// </summary>
public class NvmlWindowsProvider : IGpuProvider
{
    private const string NvmlDll = "nvml.dll";
    private IntPtr _deviceHandle;
    private bool _isInitialized;
    private uint _detectedVoltageFieldId = 156;
    private string _gpuName = "Unknown";
    private string _subsystemId = "";
    private bool _isEstimatedVoltage;

    private readonly HardwareDbManager? _hardwareDb;

    // --- NVML 結構定義 ---
    [StructLayout(LayoutKind.Explicit)]
    public struct NvmlValue_t
    {
        [FieldOffset(0)] public double DoubleValue;
        [FieldOffset(0)] public uint UiValue;
        [FieldOffset(0)] public int IValue;
        [FieldOffset(0)] public long SllValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlFieldValue_t
    {
        public uint FieldId;
        public uint Unused;
        public long Timestamp;
        public int LatencyUsec;
        public int ValueType;
        public int Status;
        public NvmlValue_t Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlPciInfo_t
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string BusId;
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PciDeviceId;      // 組合的 Device ID
        public uint PciSubsystemId;   // T2-4: Subsystem ID
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
    }

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

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetFieldValues")]
    private static extern int nvmlDeviceGetFieldValues(IntPtr device, int valuesCount, ref NvmlFieldValue_t values);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPciInfo_v3")]
    private static extern int nvmlDeviceGetPciInfo(IntPtr device, ref NvmlPciInfo_t pci);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerUsage")]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

    public bool IsAvailable => _isInitialized;
    public bool IsEstimatedVoltage => _isEstimatedVoltage;
    public string SubsystemId => _subsystemId;

    public NvmlWindowsProvider()
    {
        try
        {
            _hardwareDb = new HardwareDbManager();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "無法初始化 HardwareDbManager，將僅使用動態偵測");
        }

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            var result = nvmlInit();
            if (result != 0)
            {
                Log.Error("NVML 初始化失敗，錯誤碼: {ErrorCode}", result);
                return;
            }

            _isInitialized = true;

            result = nvmlDeviceGetHandleByIndex(0, out _deviceHandle);
            if (result != 0)
            {
                Log.Error("無法取得 GPU 裝置，錯誤碼: {ErrorCode}", result);
                _isInitialized = false;
                return;
            }

            // 讀取 GPU 名稱
            var nameBuilder = new StringBuilder(64);
            nvmlDeviceGetName(_deviceHandle, nameBuilder, (uint)nameBuilder.Capacity);
            _gpuName = nameBuilder.ToString();

            // T2-4: 讀取 PCI Subsystem ID
            ReadPciSubsystemId();

            // T2-3: 三段式 Field ID 偵測
            DetectVoltageFieldId();

            Log.Information(
                "NVML 初始化完成 | GPU: {GpuName} | SSID: {SubsystemId} | Field ID: {FieldId} | 估算模式: {IsEstimated}",
                _gpuName, _subsystemId, _detectedVoltageFieldId, _isEstimatedVoltage);
        }
        catch (DllNotFoundException ex)
        {
            Log.Error(ex, "找不到 nvml.dll，請確認已安裝 NVIDIA 驅動程式");
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "NVML 初始化時發生未預期的錯誤");
            _isInitialized = false;
        }
    }

    /// <summary>
    /// T2-4: 讀取 PCI Subsystem ID
    /// </summary>
    private void ReadPciSubsystemId()
    {
        try
        {
            var pciInfo = new NvmlPciInfo_t();
            var result = nvmlDeviceGetPciInfo(_deviceHandle, ref pciInfo);

            if (result == 0)
            {
                // Subsystem ID 格式: "VendorId:DeviceId" (16進位)
                var subsystemVendor = (pciInfo.PciSubsystemId >> 16) & 0xFFFF;
                var subsystemDevice = pciInfo.PciSubsystemId & 0xFFFF;
                _subsystemId = $"{subsystemVendor:X4}:{subsystemDevice:X4}";

                Log.Information(
                    "[PCI Info] Device ID: {DeviceId:X8}, Subsystem ID: {SubsystemId}",
                    pciInfo.PciDeviceId, _subsystemId);
            }
            else
            {
                Log.Warning("無法讀取 PCI 資訊，錯誤碼: {ErrorCode}", result);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "讀取 PCI Subsystem ID 時發生錯誤");
        }
    }

    /// <summary>
    /// T2-3: 三段式 Field ID 偵測邏輯
    /// 1. 優先查詢硬體資料庫
    /// 2. 動態掃描 Field ID 150-165
    /// 3. Fallback 到功耗估算模式
    /// </summary>
    private void DetectVoltageFieldId()
    {
        _isEstimatedVoltage = false;

        // === 第一優先：查詢硬體資料庫 ===
        if (_hardwareDb != null && !string.IsNullOrEmpty(_subsystemId))
        {
            var verifiedFieldId = _hardwareDb.GetVerifiedFieldId(_subsystemId);
            if (verifiedFieldId.HasValue)
            {
                _detectedVoltageFieldId = verifiedFieldId.Value;
                Log.Information("[三段式偵測] 第一優先：使用硬體資料庫映射 -> Field ID {FieldId}", _detectedVoltageFieldId);
                return;
            }
        }

        // === 第二優先：動態掃描 ===
        Log.Information("[三段式偵測] 第二優先：開始動態掃描 Field ID 150-165...");
        for (uint fieldId = 150; fieldId <= 165; fieldId++)
        {
            var field = new NvmlFieldValue_t { FieldId = fieldId };
            if (nvmlDeviceGetFieldValues(_deviceHandle, 1, ref field) == 0 && field.Status == 0)
            {
                float value = ExtractValue(field);
                if (value > 11.0f && value < 13.0f)
                {
                    _detectedVoltageFieldId = fieldId;
                    Log.Information(
                        "[三段式偵測] 第二優先：動態掃描成功 -> Field ID {FieldId}, 數值: {Value:F3}V",
                        fieldId, value);

                    // 自動新增到硬體資料庫 (如果有 Subsystem ID)
                    if (_hardwareDb != null && !string.IsNullOrEmpty(_subsystemId))
                    {
                        _hardwareDb.AddOrUpdateMapping(new GpuMapping
                        {
                            SubsystemId = _subsystemId,
                            FieldId = fieldId,
                            Model = _gpuName,
                            Vendor = "Auto-detected",
                            Notes = "由動態掃描自動新增"
                        });
                    }
                    return;
                }
            }
        }

        // === 第三優先：Fallback 到功耗估算模式 ===
        Log.Warning("[三段式偵測] 第三優先：無法偵測電壓 Field ID，切換至功耗估算模式");
        _isEstimatedVoltage = true;
        _detectedVoltageFieldId = 0; // 標記為使用估算模式
    }

    private float ExtractValue(NvmlFieldValue_t field)
    {
        return field.ValueType switch
        {
            0 => (float)field.Value.DoubleValue,
            3 => field.Value.SllValue / 1000.0f,
            _ => (float)field.Value.UiValue
        };
    }

    public string GetGpuName() => _isInitialized ? _gpuName : "N/A";

    public GpuReading GetCurrentReading()
    {
        if (!_isInitialized)
            return new GpuReading(0, 0, DateTime.Now);

        // 讀取溫度
        nvmlDeviceGetTemperature(_deviceHandle, 0, out uint temp);

        // 讀取電壓
        float voltage;
        if (_isEstimatedVoltage)
        {
            // Fallback: 使用功耗估算電壓 (非常粗略)
            voltage = EstimateVoltageFromPower();
        }
        else
        {
            var field = new NvmlFieldValue_t { FieldId = _detectedVoltageFieldId };
            voltage = (nvmlDeviceGetFieldValues(_deviceHandle, 1, ref field) == 0 && field.Status == 0)
                ? ExtractValue(field)
                : 0;
        }

        return new GpuReading(voltage, temp, DateTime.Now);
    }

    /// <summary>
    /// Fallback: 從功耗粗略估算電壓 (僅供參考，非實際電壓)
    /// </summary>
    private float EstimateVoltageFromPower()
    {
        try
        {
            if (nvmlDeviceGetPowerUsage(_deviceHandle, out uint powerMilliWatts) == 0)
            {
                // 假設電流為 30A (RTX 50 系列典型值)，V = P / I
                // 這只是非常粗略的估算
                float powerWatts = powerMilliWatts / 1000.0f;
                float estimatedCurrent = 30.0f;
                float estimatedVoltage = powerWatts / estimatedCurrent;

                // 限制在合理範圍內
                return Math.Clamp(estimatedVoltage, 10.0f, 14.0f);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "功耗估算失敗");
        }

        return 12.0f; // 回傳預設值
    }

    public void Dispose()
    {
        if (_isInitialized)
        {
            nvmlShutdown();
            _isInitialized = false;
            Log.Information("NVML 已安全關閉");
        }
    }
}
