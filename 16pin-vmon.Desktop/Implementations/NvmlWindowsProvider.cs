using System;
using System.Runtime.InteropServices;
using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon.Desktop.Implementations
{
    public class NvmlWindowsProvider : IGpuProvider
    {
        private const string NvmlDll = "nvml.dll";
        private IntPtr _deviceHandle;
        private bool _isInitialized;
		
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
			public NvmlValue_t Value; // 使用 Explicit 結構
		}
		
		// 關鍵的 Field ID (依據 NVIDIA 50 系列最新規範)
		// 156 或是 157 通常對應到 12V 軌道的輸入電壓
		// todo: 
		// 1. 實作一個「掃描功能」，在 Debug 模式下印出所有可能的 Field ID 數值，這對 RTX 50 系列的初期除錯非常有幫助
		private const uint NVML_FI_DEV_VOLTAGE_GRAPHICS_12V = 156;

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
        private static extern int nvmlDeviceGetName(IntPtr device, System.Text.StringBuilder name, uint length);

        // 注意：讀取 16-pin 電壓通常需要透過 nvmlDeviceGetFieldValues 並使用特定的 Field ID
	
		[DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetFieldValues")]
		private static extern int nvmlDeviceGetFieldValues(IntPtr device, int valuesCount, ref NvmlFieldValue_t values);		
		
        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerUsage")]
        private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

        public bool IsAvailable => _isInitialized;

        public NvmlWindowsProvider()
        {
            try
            {
                if (nvmlInit() == 0)
                {
                    _isInitialized = true;
                    // 預設抓取第一張顯卡 (Index 0)
                    nvmlDeviceGetHandleByIndex(0, out _deviceHandle);
                }
            }
            catch
            {
                _isInitialized = false;
            }
        }

        public string GetGpuName()
        {
            if (!_isInitialized) return "N/A";
            var sb = new System.Text.StringBuilder(64);
            nvmlDeviceGetName(_deviceHandle, sb, (uint)sb.Capacity);
            return sb.ToString();
        }

        public GpuReading GetCurrentReading()
        {
            if (!_isInitialized) return new GpuReading(0, 0, DateTime.Now);

            // 1. 讀取溫度 (0 代表 GPU 核心溫度)
            nvmlDeviceGetTemperature(_deviceHandle, 0, out uint temp);

            // 2. 讀取電壓 (這裡以基礎讀取為例)
            // 實務上 16-pin 電壓需要呼叫 nvmlDeviceGetFieldValues 並傳入 
            // NVML_FI_DEV_VOLTAGE_GRAPHICS_12V 等常數，這部分需要定義結構體
            float voltage = Fetch16PinVoltage(); 

            return new GpuReading(voltage, (float)temp, DateTime.Now);
        }

		private float Fetch16PinVoltage()
		{
			if (!_isInitialized) return 0;

			var field = new NvmlFieldValue_t { FieldId = NVML_FI_DEV_VOLTAGE_GRAPHICS_12V };
			int result = nvmlDeviceGetFieldValues(_deviceHandle, 1, ref field);

			if (result == 0 && field.Status == 0)
			{
				// NVML 的電壓值通常以毫伏 (mV) 存放在 Value 欄位
				// 需要依據 ValueType 轉換，這裡簡化為直接讀取
				// To-do:
				// Fetch16PinVoltage 中的 field.Value / 1000.0f 假設了回傳值一定是整數毫伏。如果 field.ValueType 顯示為 Double，這行會失效，需修正
				return field.Value / 1000.0f; 
			}
			
			return 12.0f; // 備援回傳
		}

        public void Dispose()
        {
            if (_isInitialized)
            {
                nvmlShutdown();
                _isInitialized = false;
            }
        }
    }
}