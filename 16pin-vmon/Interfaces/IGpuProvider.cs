using System;

namespace _16pin_vmon.Core.Interfaces;

/// <summary>
/// GPU 讀數資料
/// </summary>
/// <param name="Voltage16Pin">16-pin 連接器電壓 (V)，若無法讀取則為估算值</param>
/// <param name="Temperature">GPU 溫度 (°C)</param>
/// <param name="Timestamp">讀取時間</param>
/// <param name="GpuCoreVoltage">GPU 核心電壓 (V)，選填，RTX 50 系列可用</param>
public record GpuReading(
    float Voltage16Pin,
    float Temperature,
    DateTime Timestamp,
    float? GpuCoreVoltage = null);

public interface IGpuProvider : IDisposable
{
    string GetGpuName();
    GpuReading GetCurrentReading(); // 核心：每秒呼叫一次獲取數據
    bool IsAvailable { get; }        // 用於偵測是否抓得到 NVML
    bool IsEstimatedVoltage { get; } // 是否為估算電壓 (無法讀取真實 16-pin 電壓)
}