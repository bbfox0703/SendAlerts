using System;

namespace _16pin_vmon.Services;

/// <summary>
/// T3-1: 設定服務介面 - 跨平台設定持久化
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 載入設定，若檔案不存在則回傳預設值
    /// </summary>
    AppSettings Load();

    /// <summary>
    /// 儲存設定至檔案
    /// </summary>
    void Save(AppSettings settings);

    /// <summary>
    /// 取得設定檔路徑
    /// </summary>
    string GetSettingsFilePath();
}

/// <summary>
/// 應用程式設定模型
/// </summary>
public class AppSettings
{
    // --- Legal & Safety ---
    /// <summary>
    /// 使用者是否已確認免責聲明 (T0-2)
    /// </summary>
    public bool DisclaimerAccepted { get; set; } = false;

    /// <summary>
    /// 免責聲明確認時間
    /// </summary>
    public DateTime? DisclaimerAcceptedAt { get; set; }

    // --- Alert Thresholds ---
    /// <summary>
    /// 電壓警報門檻 (低於此值觸發)
    /// </summary>
    public float VoltageThreshold { get; set; } = 11.8f;

    /// <summary>
    /// 溫度警報門檻 (高於此值觸發)
    /// </summary>
    public float TemperatureThreshold { get; set; } = 88.0f;

    /// <summary>
    /// 滑動視窗秒數
    /// </summary>
    public int AlertWindowSeconds { get; set; } = 3;

    /// <summary>
    /// 滑動視窗內觸發次數
    /// </summary>
    public int AlertTriggerCount { get; set; } = 2;

    // --- Sampling ---
    /// <summary>
    /// 取樣間隔（秒）
    /// </summary>
    public int SamplingIntervalSeconds { get; set; } = 1;

    // --- UI ---
    /// <summary>
    /// 語系設定 (null = 自動偵測)
    /// </summary>
    public string? Language { get; set; } = null;

    // --- Data Export ---
    /// <summary>
    /// 是否啟用 CSV 匯出
    /// </summary>
    public bool CsvExportEnabled { get; set; } = false;

    /// <summary>
    /// CSV 保留筆數
    /// </summary>
    public int CsvMaxRows { get; set; } = 10000;
}
