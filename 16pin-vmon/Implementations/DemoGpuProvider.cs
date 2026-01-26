using System;
using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon.Implementations;

/// <summary>
/// Demo/Mock GPU Provider for testing without actual NVML hardware.
/// Generates simulated voltage and temperature data.
/// </summary>
public class DemoGpuProvider : IGpuProvider
{
    private readonly Random _random = new();
    private float _baseVoltage = 12.0f;
    private float _baseTemp = 65.0f;

    public bool IsAvailable => true;
    public bool IsEstimatedVoltage => true; // Demo 模式的數值為模擬值

    public string GetGpuName() => "Demo GPU (No NVML)";

    public GpuReading GetCurrentReading()
    {
        // Simulate slight fluctuations around base values
        float voltage = _baseVoltage + (float)(_random.NextDouble() * 0.4 - 0.2);
        float temp = _baseTemp + (float)(_random.NextDouble() * 10 - 5);

        // Clamp to realistic ranges
        voltage = Math.Clamp(voltage, 11.0f, 13.0f);
        temp = Math.Clamp(temp, 30.0f, 95.0f);

        return new GpuReading(voltage, temp, DateTime.Now);
    }

    public void Dispose()
    {
        // No resources to release for demo provider
    }
}
