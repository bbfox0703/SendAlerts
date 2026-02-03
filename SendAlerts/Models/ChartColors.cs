namespace SendAlerts.Models;

/// <summary>
/// 現代動漫風格配色方案：低飽和、高質感、中性調。
/// 所有 UI 顏色集中管理於此。
/// </summary>
public static class ChartColors
{
    // ── Chart Line Colors ──────────────────────────────────────────────
    // 依序為：青瓷綠 (Utilization), 暮色紅 (Temp), 靛灰藍 (Watts), 琥珀沙 (Aux)
    public static readonly string[] SlotLineColors = [
        "#4A9D8F", // 翡翠青瓷 (中性綠，比 LimeGreen 穩重許多)
        "#B45B5B", // 暮色緋紅 (乾燥玫瑰調的紅，警示但不刺眼)
        "#6A89A7", // 靛藍板岩 (帶灰質感的藍，非常適合科技感)
        "#C29A4F"  // 琥珀土黃 (中性黃，像舊時代機器的指示燈)
    ];

    // ── Chart Fill Gradient ────────────────────────────────────────────
    // 漸層固定於圖表框架 (Y=max → Y=0)，類似 Windows 工作管理員風格
    public const byte FillAlphaTop = 45;     // Y=max 處 (線條最高時) 較濃
    public const byte FillAlphaBottom = 6;   // Y=0 處 (底部) 幾乎透明

    // ── Chart Area ─────────────────────────────────────────────────────
    public const string ChartBackground = "#161618";  // 稍微帶一點點深藍/深紫的極黑
    public const string GridLine = "#2A2A2B";          // 網格線要隱約可見
    public const string AxisColor = "#555555";         // 座標軸線調暗
    public const string TickLabel = "#888888";         // 數值標記使用中灰色

    // ── Crosshair / Tooltip ────────────────────────────────────────────
    public const string CrosshairLine = "#FFFFFF";     // 十字線
    public const byte CrosshairLineAlpha = 120;
    public const string CrosshairText = "#FFFFFF";     // 十字線文字
    public const byte CrosshairTextBgAlpha = 200;      // 十字線文字背景 (用 line color)
    public const string TooltipFg = "#FFFFFF";         // Tooltip 文字
    public const string TooltipBg = "#252526";         // Tooltip 背景
    public const byte TooltipBgAlpha = 220;

    // ── Main UI: Navigation Bar ────────────────────────────────────────
    public const string NavBarBg = "#2b2b2b";
    public const string GpuNameFg = "#FFFFFF";
    public const string PowerLimitFg = "#888888";
    public const string DisplayModeHintBg = "#3a3a00";
    public const string DisplayModeHintFg = "#ffcc00";

    // ── Main UI: Status Bar ────────────────────────────────────────────
    public const string StatusBarBg = "#1e1e1e";
    public const string StatusBarBorder = "#333333";
    public const string StatusTextFg = "#aaaaaa";
    public const string AvgSummaryFg = "#888888";
    public const string AlertCountFg = "#666666";
    public const string VersionFg = "#555555";

    // ── Main UI: Chart Slot ────────────────────────────────────────────
    public const string SlotBorderBg = "#161618";      // 與 ChartBackground 一致

    // ── Main UI: Alert History ─────────────────────────────────────────
    public const string HistoryTimeFg = "#888888";
    public const string HistoryTextFg = "#cccccc";
    public const string HistoryPanelBg = "#161618";

    // ── Dialog ─────────────────────────────────────────────────────────
    public const string DialogBg = "#2b2b2b";
    public const string DialogSectionBg = "#222222";
    public const string DialogLabelFg = "#aaaaaa";
    public const string DialogHeadingFg = "#FFFFFF";
    public const string DialogListBg = "#161618";

    // ── Converters / Status Indicators ─────────────────────────────────
    public const string StatusActive = "#32CD32";      // LimeGreen
    public const string StatusInactive = "#808080";    // Gray
    public const string AlertRed = "#FF0000";
    public const string NormalWhite = "#FFFFFF";
    public const string EnabledGreen = "#2E7D32";      // RGB(46,125,50)
    public const string DisabledGray = "#787878";      // RGB(120,120,120)
    public const string EnabledFg = "#FFFFFF";
    public const string DisabledFg = "#696969";        // DimGray

    // ── Toast Notification ─────────────────────────────────────────────
    public const string ToastBg = "#2D2D30";           // RGB(45,45,48)
    public const string ToastTitleFg = "#FFFFFF";
    public const string ToastMessageFg = "#C8C8C8";    // RGB(200,200,200)
}
