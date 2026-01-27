using System;
using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon.Implementations;

/// <summary>
/// Demo/Mock GPU Provider for testing without actual NVML hardware.
/// Generates simulated power and temperature data.
/// </summary>
public class DemoGpuProvider : IGpuProvider
{
    private readonly Random _random = new();
    private float _baseUtilization = 50.0f;
    private float _baseTemp = 65.0f;
    private float _basePower = 150.0f;

    public bool IsAvailable => true;
    public float PowerLimit => 450.0f; // Demo power limit

    // 硬體模式與動態標籤 (GPU 模式)
    public HardwareMode Mode => HardwareMode.Gpu;
    public string PrimaryMetricLabel => "GPU Utilization";
    public string TemperatureLabel => "GPU Temperature";
    public string SecondaryMetricLabel => "Power Usage";
    public string SecondaryMetricUnit => "W";

    public string GetGpuName() => "Demo GPU (No NVML)";

    public GpuReading GetCurrentReading()
    {
        // Simulate slight fluctuations around base values
        float utilization = _baseUtilization + (float)(_random.NextDouble() * 30 - 15);
        float temp = _baseTemp + (float)(_random.NextDouble() * 10 - 5);
        float power = _basePower + (float)(_random.NextDouble() * 50 - 25);

        // Clamp to realistic ranges
        utilization = Math.Clamp(utilization, 0.0f, 100.0f);
        temp = Math.Clamp(temp, 30.0f, 95.0f);
        power = Math.Clamp(power, 50.0f, 500.0f);

        return new GpuReading(utilization, temp, power, DateTime.Now);
    }

    public void Dispose()
    {
        // No resources to release for demo provider
    }
}
