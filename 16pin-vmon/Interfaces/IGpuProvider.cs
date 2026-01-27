using System;

namespace _16pin_vmon.Core.Interfaces;

/// <summary>
/// 硬體監控模式
/// </summary>
public enum HardwareMode
{
    Gpu,        // NVIDIA GPU 監控模式
    CpuNetwork  // CPU/網路 fallback 模式
}

/// <summary>
/// GPU 讀數資料 (或 CPU/Network 讀數)
/// </summary>
/// <param name="GpuUtilization">GPU/CPU 使用率 (%)</param>
/// <param name="Temperature">GPU/CPU 溫度 (°C)</param>
/// <param name="PowerUsage">GPU 功耗 (W) 或網路 I/O (KB/s)</param>
/// <param name="Timestamp">讀取時間</param>
public record GpuReading(
    float GpuUtilization,
    float Temperature,
    float PowerUsage,
    DateTime Timestamp);

public interface IGpuProvider : IDisposable
{
    string GetGpuName();
    GpuReading GetCurrentReading(); // 核心：每秒呼叫一次獲取數據
    bool IsAvailable { get; }        // 用於偵測是否抓得到硬體
    float PowerLimit { get; }        // GPU 功耗限制 (W) 或網路頻寬上限 (KB/s)

    // 硬體模式與動態標籤
    HardwareMode Mode { get; }
    string PrimaryMetricLabel { get; }    // "GPU Utilization" 或 "CPU Utilization"
    string TemperatureLabel { get; }      // "GPU Temperature" 或 "CPU Temperature"
    string SecondaryMetricLabel { get; }  // "Power Usage" 或 "Network I/O"
    string SecondaryMetricUnit { get; }   // "W" 或 "KB/s"
}