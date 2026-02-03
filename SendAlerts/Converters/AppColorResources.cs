using Avalonia.Media;
using SendAlerts.Models;

namespace SendAlerts.Converters;

/// <summary>
/// Exposes ChartColors constants as Avalonia Brush/Color resources
/// for binding in AXAML via {x:Static conv:AppColorResources.XXX}.
/// </summary>
public static class AppColorResources
{
    // Navigation Bar
    public static readonly SolidColorBrush NavBarBg = Parse(ChartColors.NavBarBg);
    public static readonly SolidColorBrush GpuNameFg = Parse(ChartColors.GpuNameFg);
    public static readonly SolidColorBrush PowerLimitFg = Parse(ChartColors.PowerLimitFg);
    public static readonly SolidColorBrush DisplayModeHintBg = Parse(ChartColors.DisplayModeHintBg);
    public static readonly SolidColorBrush DisplayModeHintFg = Parse(ChartColors.DisplayModeHintFg);

    // Status Bar
    public static readonly SolidColorBrush StatusBarBg = Parse(ChartColors.StatusBarBg);
    public static readonly SolidColorBrush StatusBarBorder = Parse(ChartColors.StatusBarBorder);
    public static readonly SolidColorBrush StatusTextFg = Parse(ChartColors.StatusTextFg);
    public static readonly SolidColorBrush AvgSummaryFg = Parse(ChartColors.AvgSummaryFg);
    public static readonly SolidColorBrush AlertCountFg = Parse(ChartColors.AlertCountFg);
    public static readonly SolidColorBrush VersionFg = Parse(ChartColors.VersionFg);

    // Chart Slot
    public static readonly SolidColorBrush SlotBorderBg = Parse(ChartColors.SlotBorderBg);

    // Alert History
    public static readonly SolidColorBrush HistoryTimeFg = Parse(ChartColors.HistoryTimeFg);
    public static readonly SolidColorBrush HistoryTextFg = Parse(ChartColors.HistoryTextFg);
    public static readonly SolidColorBrush HistoryPanelBg = Parse(ChartColors.HistoryPanelBg);

    // Dialog
    public static readonly SolidColorBrush DialogBg = Parse(ChartColors.DialogBg);
    public static readonly SolidColorBrush DialogSectionBg = Parse(ChartColors.DialogSectionBg);
    public static readonly SolidColorBrush DialogLabelFg = Parse(ChartColors.DialogLabelFg);
    public static readonly SolidColorBrush DialogHeadingFg = Parse(ChartColors.DialogHeadingFg);
    public static readonly SolidColorBrush DialogListBg = Parse(ChartColors.DialogListBg);

    private static SolidColorBrush Parse(string hex) => new(Color.Parse(hex));
}
