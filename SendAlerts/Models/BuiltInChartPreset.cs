namespace SendAlerts.Models;

/// <summary>
/// Built-in chart presets for the flexible chart system.
/// Each preset maps to a specific hardware metric from IGpuProvider.
/// </summary>
public enum BuiltInChartPreset
{
    None = 0,
    GpuCoreUtilization,  // Y: 0-100 fixed
    GpuTemperature,      // Y: 0-100 fixed
    GpuPowerUsage,       // Y: dynamic, 50W step
    CpuUtilization,      // Y: 0-100 fixed
    MemoryUsage,         // Y: 0-100 fixed
    NetworkIO            // Y: dynamic, 100 step, KB->MB switch
}
