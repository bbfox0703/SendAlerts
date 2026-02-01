using System.Collections.Generic;

namespace SendAlerts.Models;

/// <summary>
/// HWiNFO Shared Memory 感測器群組 (對應 HWiNFO sensor section)
/// </summary>
public record HwinfoSensorGroup(string SensorName, List<HwinfoSensorEntry> Entries);

/// <summary>
/// 單一感測器項目
/// </summary>
public record HwinfoSensorEntry(
    string Label,       // 使用者自訂顯示名稱
    string LabelOrig,   // 原始名稱 (e.g. "GPU Power")
    string Unit,        // 單位 (V, W, °C, %, MHz...)
    double Value,       // 當前值
    double ValueMin,
    double ValueMax,
    double ValueAvg);

/// <summary>
/// ComboBox 用的扁平化感測器項目
/// </summary>
public class HwinfoSensorItem
{
    public string SensorName { get; }
    public string LabelOrig { get; }
    public string Label { get; }
    public string Unit { get; }
    public string DisplayName { get; }

    public HwinfoSensorItem(string sensorName, string labelOrig, string label, string unit)
    {
        SensorName = sensorName;
        LabelOrig = labelOrig;
        Label = label;
        Unit = unit;
        DisplayName = $"{label} ({unit}) [{sensorName}]";
    }

    public override string ToString() => DisplayName;
}
