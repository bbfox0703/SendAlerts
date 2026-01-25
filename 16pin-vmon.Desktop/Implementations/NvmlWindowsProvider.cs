using System;
using System.Runtime.InteropServices;
using System.Text;
using _16pin_vmon.Core.Interfaces;
using Serilog;

namespace _16pin_vmon.Desktop.Implementations
{
    public class NvmlWindowsProvider : IGpuProvider
    {
        private const string NvmlDll = "nvml.dll";
        private IntPtr _deviceHandle;
        private bool _isInitialized;
        private uint _detectedVoltageFieldId = 156; // 預設值，若掃描失敗則回退至此

        // --- NVML 結構定義 (模擬 C 語言 Union) ---
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
            public int ValueType; // 0: double, 1: uint32, 2: int32, 3: sll (long)
            public int Status;    // 0: Success
            public NvmlValue_t Value;
        }

        // --- NVML P/Invoke 宣告 ---
        [DllImport(NvmlDll, EntryPoint = "nvmlInit_v2")] private static extern int nvmlInit();
        [DllImport(NvmlDll, EntryPoint = "nvmlShutdown")] private static extern int nvmlShutdown();
        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);
        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetTemperature")] private static extern int nvmlDeviceGetTemperature(IntPtr device, uint type, out uint temp);
        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetName")] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetFieldValues")] private static extern int nvmlDeviceGetFieldValues(IntPtr device, int valuesCount, ref NvmlFieldValue_t values);

        public bool IsAvailable => _isInitialized;

        public NvmlWindowsProvider()
        {
            try
            {
                if (nvmlInit() == 0)
                {
                    _isInitialized = true;
                    if (nvmlDeviceGetHandleByIndex(0, out _deviceHandle) == 0)
                    {
                        // 關鍵：啟動時自動探索最適合 RTX 50 的 Field ID
                        DiscoverBestVoltageField();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NVML 初始化失敗，請檢查驅動程式是否支援。");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// 自動掃描 ID 150-165 區間，尋找數值在 11V~13V 之間的感測器
        /// </summary>
        private void DiscoverBestVoltageField()
        {
            Log.Information("正在掃描 RTX 50 16-pin 電壓感測器 ID...");
            for (uint i = 150; i < 165; i++)
            {
                var field = new NvmlFieldValue_t { FieldId = i };
                if (nvmlDeviceGetFieldValues(_deviceHandle, 1, ref field) == 0 && field.Status == 0)
                {
                    float val = ExtractValue(field);
                    if (val > 11.0f && val < 13.0f)
                    {
                        _detectedVoltageFieldId = i;
                        Log.Information("成功偵測到 16-pin 電壓 ID: {Id}，目前數值: {Val}V", i, val);
                        return;
                    }
                }
            }
            Log.Warning("未找到精確電壓 ID，將使用預設 ID 156。");
        }

        private float ExtractValue(NvmlFieldValue_t field)
        {
            // 修正：根據 ValueType 動態決定如何讀取 Value
            return field.ValueType switch
            {
                0 => (float)field.Value.DoubleValue,      // Double
                3 => field.Value.SllValue / 1000.0f,     // Signed Long Long (通常以 mV 為單位)
                _ => (float)field.Value.UiValue          // 其他類型當作原始數值
            };
        }

        public string GetGpuName()
        {
            if (!_isInitialized) return "N/A";
            var sb = new StringBuilder(64);
            nvmlDeviceGetName(_deviceHandle, sb, (uint)sb.Capacity);
            return sb.ToString();
        }

        public GpuReading GetCurrentReading()
        {
            if (!_isInitialized) return new GpuReading(0, 0, DateTime.Now);

            // 讀取溫度
            nvmlDeviceGetTemperature(_deviceHandle, 0, out uint temp);

            // 讀取電壓 (使用探索出的 ID)
            var field = new NvmlFieldValue_t { FieldId = _detectedVoltageFieldId };
            float voltage = 0;
            if (nvmlDeviceGetFieldValues(_deviceHandle, 1, ref field) == 0 && field.Status == 0)
            {
                voltage = ExtractValue(field);
            }

            return new GpuReading(voltage, (float)temp, DateTime.Now);
        }

        public void Dispose()
        {
            if (_isInitialized)
            {
                nvmlShutdown();
                _isInitialized = false;
                Log.Information("NVML 已安全關閉。");
            }
        }
    }
}