namespace SendAlerts.Models;

/// <summary>
/// Source type for a chart slot.
/// </summary>
public enum ChartSlotSourceType
{
    Off = 0,
    BuiltIn = 1,
    External = 2
}

/// <summary>
/// Configuration for a single chart slot.
/// Persisted in AppSettings.ChartSlots.
/// </summary>
public class ChartSlotConfig
{
    public ChartSlotSourceType SourceType { get; set; } = ChartSlotSourceType.BuiltIn;
    public BuiltInChartPreset BuiltInPreset { get; set; } = BuiltInChartPreset.None;
    public ChartSourceType ExternalSource { get; set; } = ChartSourceType.Off;
    public string? SensorName { get; set; }
    public string? SensorEntry { get; set; }

    public ChartSlotConfig Clone() => new()
    {
        SourceType = SourceType,
        BuiltInPreset = BuiltInPreset,
        ExternalSource = ExternalSource,
        SensorName = SensorName,
        SensorEntry = SensorEntry
    };
}
