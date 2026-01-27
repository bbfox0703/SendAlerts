using System;
using System.Collections.Generic;
using _16pin_vmon.Models;

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

    // --- Alert Actions (T4-1~T4-5) ---
    /// <summary>
    /// T4-5: Debug 模式 - 所有警報動作僅記錄不執行
    /// </summary>
    public bool AlertActionsDebugMode { get; set; } = false;

    /// <summary>
    /// T4-1: 是否啟用命令列警報動作
    /// </summary>
    public bool CommandLineAlertEnabled { get; set; } = false;

    /// <summary>
    /// T4-1: 警報觸發時執行的命令
    /// 支援變數: {voltage}, {temperature}, {gpu_name}, {alert_type}, {timestamp}
    /// Windows 範例: powershell -c "Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).Speak('Alert')"
    /// Linux 範例: notify-send "GPU Alert" "{alert_type}: Voltage={voltage}V"
    /// </summary>
    public string CommandLineAlertCommand { get; set; } = string.Empty;

    /// <summary>
    /// T4-1: 命令執行冷卻時間（秒）
    /// </summary>
    public int CommandLineAlertCooldownSeconds { get; set; } = 30;

    /// <summary>
    /// T4-2: 是否啟用 Telegram 警報
    /// </summary>
    public bool TelegramAlertEnabled { get; set; } = false;

    /// <summary>
    /// T4-2: Telegram Bot Token
    /// </summary>
    public string TelegramBotToken { get; set; } = string.Empty;

    /// <summary>
    /// T4-2: Telegram Chat ID
    /// </summary>
    public string TelegramChatId { get; set; } = string.Empty;

    /// <summary>
    /// T4-3: 是否啟用 LINE Notify 警報
    /// </summary>
    public bool LineNotifyAlertEnabled { get; set; } = false;

    /// <summary>
    /// T4-3: LINE Notify Access Token
    /// </summary>
    public string LineNotifyAccessToken { get; set; } = string.Empty;

    // ==========================================================================
    // TA4-1: Alert Center 設定 (新架構)
    // ==========================================================================

    /// <summary>
    /// 設定檔版本號 (用於 TA4-2 版本遷移)
    /// </summary>
    public int SettingsVersion { get; set; } = 1;

    /// <summary>
    /// TA4-1: 警報動作設定清單 (多實例支援)
    /// 每個 AlertActionConfig 代表一個獨立的警報動作實例
    /// </summary>
    public List<AlertActionConfig> AlertActions { get; set; } = new();

    /// <summary>
    /// TA4-1: 警報群組設定清單
    /// 每個 AlertGroup 可包含多個 ActionInstanceId 引用
    /// </summary>
    public List<AlertGroup> AlertGroups { get; set; } = new();

    /// <summary>
    /// TA4-1: 是否使用新版 Alert Center 架構
    /// false = 使用舊版 T4 設定 (向後相容)
    /// true = 使用新版 AlertActions + AlertGroups
    /// </summary>
    public bool UseAlertCenterMode { get; set; } = false;
}
