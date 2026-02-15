using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SendAlerts.Core.Interfaces;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// T4-2/TA2-1: Telegram 警報動作 - 透過 Telegram Bot API 發送警報訊息
/// 設定步驟:
/// 1. 與 @BotFather 對話建立 Bot，取得 Bot Token
/// 2. 與 Bot 對話後，訪問 https://api.telegram.org/bot{TOKEN}/getUpdates 取得 Chat ID
/// </summary>
public class TelegramAlertAction : IAlertAction
{
    private readonly ISettingsService? _settingsService;
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTime _lastExecutionTime = DateTime.MinValue;

    private const int DefaultCooldownSeconds = AppConstants.DefaultCooldownSeconds;
    private const string TelegramApiBaseUrl = "https://api.telegram.org/bot";

    // TA2-1: 新增介面成員
    public string InstanceId { get; set; } = "Telegram_Default";
    public AlertActionType ActionType => AlertActionType.Telegram;
    public string DisplayName => string.IsNullOrWhiteSpace(InstanceId) ? "Telegram" : InstanceId;
    public bool IsEnabled { get; set; }

    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool DebugMode { get; set; } = false;
    public int CooldownSeconds { get; set; } = DefaultCooldownSeconds;

    /// <summary>
    /// 建構子 - 從設定服務載入 (舊版相容)
    /// </summary>
    public TelegramAlertAction(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();
    }

    /// <summary>
    /// 建構子 - 直接指定參數 (多實例支援)
    /// </summary>
    public TelegramAlertAction(string instanceId, string botToken, string chatId, int cooldownSeconds = DefaultCooldownSeconds, bool debugMode = false)
    {
        InstanceId = instanceId;
        BotToken = botToken;
        ChatId = chatId;
        CooldownSeconds = cooldownSeconds;
        DebugMode = debugMode;
        IsEnabled = true;
    }

    private void LoadSettings()
    {
        if (_settingsService == null) return;
        var settings = _settingsService.Load();
        IsEnabled = settings.TelegramAlertEnabled;
        BotToken = settings.TelegramBotToken;
        ChatId = settings.TelegramChatId;
        DebugMode = settings.AlertActionsDebugMode;
    }

    /// <summary>
    /// TA2-1: 驗證設定是否有效
    /// </summary>
    public AlertActionValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            return AlertActionValidationResult.Invalid("Bot Token 不可為空");

        if (string.IsNullOrWhiteSpace(ChatId))
            return AlertActionValidationResult.Invalid("Chat ID 不可為空");

        return AlertActionValidationResult.Valid();
    }

    public async Task<AlertActionExecuteResult> ExecuteAsync(string message)
    {
        if (!IsEnabled)
            return AlertActionExecuteResult.Skipped("動作已停用");

        if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(ChatId))
        {
            Log.Warning("[TelegramAlertAction] Bot Token 或 Chat ID 未設定，無法發送訊息");
            return AlertActionExecuteResult.Fail("Bot Token 或 Chat ID 未設定");
        }

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            var msg = $"冷卻中，跳過發送 (剩餘 {CooldownSeconds - elapsed.TotalSeconds:F0} 秒)";
            Log.Debug("[TelegramAlertAction] {Message}", msg);
            return AlertActionExecuteResult.Skipped(msg);
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[TelegramAlertAction][DEBUG MODE] 將發送訊息: {Message}", message);
            _lastExecutionTime = DateTime.Now;
            return AlertActionExecuteResult.Ok("[DEBUG] 模擬發送成功");
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            var formattedMessage = FormatMessage(message);
            Log.Information("[TelegramAlertAction] 發送 Telegram 訊息...");

            return await SendMessageAsync(formattedMessage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TelegramAlertAction] 發送 Telegram 訊息時發生例外");
            return AlertActionExecuteResult.Fail($"例外: {ex.Message}");
        }
    }

    private async Task<AlertActionExecuteResult> SendMessageAsync(string text)
    {
        var url = $"{TelegramApiBaseUrl}{BotToken}/sendMessage";

        var payload = new
        {
            chat_id = ChatId,
            text = text,
            parse_mode = "HTML"
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await SharedHttpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                Log.Information("[TelegramAlertAction] Telegram 訊息發送成功");
                return AlertActionExecuteResult.Ok($"HTTP {statusCode} OK");
            }

            // 嘗試解析 Telegram API 錯誤訊息
            var errorDetail = responseBody;
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("description", out var desc))
                    errorDetail = desc.GetString() ?? responseBody;
            }
            catch { /* 無法解析就用原始 body */ }

            Log.Warning("[TelegramAlertAction] API 回應錯誤: {StatusCode} - {Body}",
                statusCode, responseBody);
            return AlertActionExecuteResult.Fail($"HTTP {statusCode}: {errorDetail}", statusCode);
        }
        catch (TaskCanceledException)
        {
            Log.Warning("[TelegramAlertAction] 請求逾時");
            return AlertActionExecuteResult.Fail("請求逾時 (10 秒)");
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "[TelegramAlertAction] 網路請求失敗");
            return AlertActionExecuteResult.Fail($"網路錯誤: {ex.Message}");
        }
    }

    /// <summary>
    /// 格式化警報訊息為 Telegram HTML 格式
    /// </summary>
    private static string FormatMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return $"<b>SendAlerts Alert</b>\n\n" +
               $"{EscapeHtml(message)}\n\n" +
               $"<i>{timestamp}</i>";
    }

    /// <summary>
    /// 跳脫 HTML 特殊字元
    /// </summary>
    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
