using System;

namespace _16pin_vmon.Core.Interfaces;

public record GpuReading(float Voltage16Pin, float Temperature, DateTime Timestamp);

public interface IGpuProvider : IDisposable
{
    string GetGpuName();
    GpuReading GetCurrentReading(); // 核心：每秒呼叫一次獲取數據
    bool IsAvailable { get; }      // 用於偵測是否抓得到 NVML
}