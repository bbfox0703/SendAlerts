using System;
using System.Collections.Generic;
using SendAlerts.Core.Interfaces;

namespace SendAlerts.Models;

/// <summary>
/// TA2-2: 警報動作設定資料模型
/// 使用扁平化結構儲存所有動作類型的設定，根據 ActionType 決定使用哪些屬性
/// </summary>
public class AlertActionConfig
{
    #region 共用屬性

    /// <summary>
    /// 實例唯一識別碼 (例如: "LINE_User1", "Telegram_Admin")
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 動作類型
    /// </summary>
    public AlertActionType ActionType { get; set; }

    /// <summary>
    /// 是否啟用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 冷卻時間（秒），避免重複執行
    /// </summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>
    /// Debug 模式：僅記錄不實際執行
    /// </summary>
    public bool DebugMode { get; set; } = false;

    #endregion

    #region CommandLine 專用屬性

    /// <summary>
    /// 要執行的命令（支援變數替換）
    /// 變數: {power}, {temperature}, {gpu_name}, {alert_type}, {timestamp}
    /// </summary>
    public string? Command { get; set; }

    #endregion

    #region Telegram 專用屬性

    /// <summary>
    /// Telegram Bot Token (從 @BotFather 取得)
    /// </summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>
    /// Telegram Chat ID (接收訊息的聊天室/群組 ID)
    /// </summary>
    public string? TelegramChatId { get; set; }

    #endregion

    #region Discord 專用屬性

    /// <summary>
    /// Discord Webhook URL (從 Discord 頻道設定 → 整合 → Webhook 取得)
    /// </summary>
    public string? DiscordWebhookUrl { get; set; }

    /// <summary>
    /// Discord Webhook 顯示的使用者名稱 (可選)
    /// </summary>
    public string? DiscordUsername { get; set; }

    #endregion

    #region Email 專用屬性 (TA2-6)

    /// <summary>
    /// SMTP 伺服器位址
    /// </summary>
    public string? SmtpHost { get; set; }

    /// <summary>
    /// SMTP 伺服器埠號 (預設: 587 for TLS, 465 for SSL)
    /// </summary>
    public int? SmtpPort { get; set; }

    /// <summary>
    /// 是否使用 SSL/TLS
    /// </summary>
    public bool? SmtpUseSsl { get; set; }

    /// <summary>
    /// SMTP 帳號
    /// </summary>
    public string? SmtpUsername { get; set; }

    /// <summary>
    /// SMTP 密碼
    /// </summary>
    public string? SmtpPassword { get; set; }

    /// <summary>
    /// 寄件者電子郵件地址
    /// </summary>
    public string? EmailFromAddress { get; set; }

    /// <summary>
    /// 寄件者顯示名稱
    /// </summary>
    public string? EmailFromName { get; set; }

    /// <summary>
    /// 收件者電子郵件地址 (多位收件者以分號分隔)
    /// </summary>
    public string? EmailToAddresses { get; set; }

    /// <summary>
    /// 郵件主旨範本 (支援變數替換)
    /// </summary>
    public string? EmailSubjectTemplate { get; set; }

    #endregion

    #region HttpWebhook 專用屬性 (TA2-7)

    /// <summary>
    /// Webhook URL
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// HTTP 方法 (GET/POST/PUT)
    /// </summary>
    public string? WebhookHttpMethod { get; set; }

    /// <summary>
    /// 自訂 HTTP Headers (JSON 格式)
    /// </summary>
    public Dictionary<string, string>? WebhookHeaders { get; set; }

    /// <summary>
    /// 請求 Body 範本 (支援變數替換，僅 POST/PUT)
    /// </summary>
    public string? WebhookBodyTemplate { get; set; }

    /// <summary>
    /// Content-Type (預設: application/json)
    /// </summary>
    public string? WebhookContentType { get; set; }

    #endregion

    #region SystemShutdown 專用屬性 (TA2-8)

    /// <summary>
    /// 關機前延遲秒數 (給使用者取消的機會)
    /// </summary>
    public int? ShutdownDelaySeconds { get; set; }

    /// <summary>
    /// 是否需要使用者確認對話框
    /// </summary>
    public bool? ShutdownRequireConfirmation { get; set; }

    /// <summary>
    /// 關機類型 (Shutdown/Restart/Hibernate/Sleep)
    /// </summary>
    public string? ShutdownType { get; set; }

    #endregion

    #region 驗證方法

    /// <summary>
    /// 驗證設定是否有效
    /// </summary>
    public AlertActionValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(InstanceId))
            return AlertActionValidationResult.Invalid("InstanceId 不可為空");

        return ActionType switch
        {
            AlertActionType.CommandLine => ValidateCommandLine(),
            AlertActionType.Telegram => ValidateTelegram(),
            AlertActionType.Discord => ValidateDiscord(),
            AlertActionType.Email => ValidateEmail(),
            AlertActionType.HttpWebhook => ValidateHttpWebhook(),
            AlertActionType.SystemShutdown => ValidateSystemShutdown(),
            _ => AlertActionValidationResult.Invalid($"不支援的動作類型: {ActionType}")
        };
    }

    private AlertActionValidationResult ValidateCommandLine()
    {
        if (string.IsNullOrWhiteSpace(Command))
            return AlertActionValidationResult.Invalid("命令不可為空");
        return AlertActionValidationResult.Valid();
    }

    private AlertActionValidationResult ValidateTelegram()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken))
            return AlertActionValidationResult.Invalid("Telegram Bot Token 不可為空");
        if (string.IsNullOrWhiteSpace(TelegramChatId))
            return AlertActionValidationResult.Invalid("Telegram Chat ID 不可為空");
        return AlertActionValidationResult.Valid();
    }

    private AlertActionValidationResult ValidateDiscord()
    {
        if (string.IsNullOrWhiteSpace(DiscordWebhookUrl))
            return AlertActionValidationResult.Invalid("Discord Webhook URL 不可為空");
        if (!Uri.TryCreate(DiscordWebhookUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return AlertActionValidationResult.Invalid("Discord Webhook URL 格式無效");
        return AlertActionValidationResult.Valid();
    }

    private AlertActionValidationResult ValidateEmail()
    {
        if (string.IsNullOrWhiteSpace(SmtpHost))
            return AlertActionValidationResult.Invalid("SMTP 伺服器位址不可為空");
        if (!SmtpPort.HasValue || SmtpPort <= 0)
            return AlertActionValidationResult.Invalid("SMTP 埠號無效");
        if (string.IsNullOrWhiteSpace(EmailFromAddress))
            return AlertActionValidationResult.Invalid("寄件者地址不可為空");
        if (string.IsNullOrWhiteSpace(EmailToAddresses))
            return AlertActionValidationResult.Invalid("收件者地址不可為空");
        return AlertActionValidationResult.Valid();
    }

    private AlertActionValidationResult ValidateHttpWebhook()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
            return AlertActionValidationResult.Invalid("Webhook URL 不可為空");
        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return AlertActionValidationResult.Invalid("Webhook URL 格式無效");
        return AlertActionValidationResult.Valid();
    }

    private AlertActionValidationResult ValidateSystemShutdown()
    {
        // SystemShutdown 沒有必填欄位，但建議設定延遲時間
        return AlertActionValidationResult.Valid();
    }

    #endregion

    #region 工廠方法

    /// <summary>
    /// 建立 CommandLine 類型的設定
    /// </summary>
    public static AlertActionConfig CreateCommandLine(string instanceId, string command, int cooldownSeconds = 30)
    {
        return new AlertActionConfig
        {
            InstanceId = instanceId,
            ActionType = AlertActionType.CommandLine,
            Command = command,
            CooldownSeconds = cooldownSeconds
        };
    }

    /// <summary>
    /// 建立 Telegram 類型的設定
    /// </summary>
    public static AlertActionConfig CreateTelegram(string instanceId, string botToken, string chatId, int cooldownSeconds = 30)
    {
        return new AlertActionConfig
        {
            InstanceId = instanceId,
            ActionType = AlertActionType.Telegram,
            TelegramBotToken = botToken,
            TelegramChatId = chatId,
            CooldownSeconds = cooldownSeconds
        };
    }

    /// <summary>
    /// 建立 Discord 類型的設定
    /// </summary>
    public static AlertActionConfig CreateDiscord(string instanceId, string webhookUrl, string? username = null, int cooldownSeconds = 30)
    {
        return new AlertActionConfig
        {
            InstanceId = instanceId,
            ActionType = AlertActionType.Discord,
            DiscordWebhookUrl = webhookUrl,
            DiscordUsername = username,
            CooldownSeconds = cooldownSeconds
        };
    }

    /// <summary>
    /// 建立 Email 類型的設定
    /// </summary>
    public static AlertActionConfig CreateEmail(
        string instanceId,
        string smtpHost,
        int smtpPort,
        string fromAddress,
        string toAddresses,
        string? username = null,
        string? password = null,
        bool useSsl = true,
        int cooldownSeconds = 30)
    {
        return new AlertActionConfig
        {
            InstanceId = instanceId,
            ActionType = AlertActionType.Email,
            SmtpHost = smtpHost,
            SmtpPort = smtpPort,
            SmtpUseSsl = useSsl,
            SmtpUsername = username,
            SmtpPassword = password,
            EmailFromAddress = fromAddress,
            EmailToAddresses = toAddresses,
            CooldownSeconds = cooldownSeconds
        };
    }

    /// <summary>
    /// 建立 HttpWebhook 類型的設定
    /// </summary>
    public static AlertActionConfig CreateHttpWebhook(
        string instanceId,
        string url,
        string httpMethod = "POST",
        string? bodyTemplate = null,
        string contentType = "application/json",
        int cooldownSeconds = 30)
    {
        return new AlertActionConfig
        {
            InstanceId = instanceId,
            ActionType = AlertActionType.HttpWebhook,
            WebhookUrl = url,
            WebhookHttpMethod = httpMethod,
            WebhookBodyTemplate = bodyTemplate,
            WebhookContentType = contentType,
            CooldownSeconds = cooldownSeconds
        };
    }

    /// <summary>
    /// 建立 SystemShutdown 類型的設定
    /// </summary>
    public static AlertActionConfig CreateSystemShutdown(
        string instanceId,
        int delaySeconds = 60,
        bool requireConfirmation = true,
        string shutdownType = "Shutdown")
    {
        return new AlertActionConfig
        {
            InstanceId = instanceId,
            ActionType = AlertActionType.SystemShutdown,
            ShutdownDelaySeconds = delaySeconds,
            ShutdownRequireConfirmation = requireConfirmation,
            ShutdownType = shutdownType
        };
    }

    #endregion
}
