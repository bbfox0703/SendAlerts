namespace SendAlerts.Models;

/// <summary>
/// 現代動漫風格配色方案：低飽和、高質感、中性調。
/// </summary>
public static class ChartColors
{
    // 依序為：青瓷綠 (Utilization), 暮色紅 (Temp), 靛灰藍 (Watts), 琥珀沙 (Aux)
    public static readonly string[] SlotLineColors = [
        "#4A9D8F", // 翡翠青瓷 (中性綠，比 LimeGreen 穩重許多)
        "#B45B5B", // 暮色緋紅 (乾燥玫瑰調的紅，警示但不刺眼)
        "#6A89A7", // 靛藍板岩 (帶灰質感的藍，非常適合科技感)
        "#C29A4F"  // 琥珀土黃 (中性黃，像舊時代機器的指示燈)
    ];

    public const byte FillAlpha = 35;          // 稍微調降透明度，讓漸層更自然
    public const string Background = "#161618"; // 稍微帶一點點深藍/深紫的極黑，更有層次
    public const string GridLine = "#2A2A2B";   // 網格線要隱約可見，不要太亮
    public const string AxisColor = "#555555";  // 座標軸線調暗
    public const string TickLabel = "#888888";  // 數值標記使用中灰色
    public const string TooltipBg = "#252526";  // Tooltip 背景與視窗區隔
}