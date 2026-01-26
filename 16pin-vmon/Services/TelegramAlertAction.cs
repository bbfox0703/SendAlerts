using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using _16pin_vmon.Core.Interfaces;
using Serilog;

namespace _16pin_vmon.Services;

/// <summary>
/// T4-2: Telegram 警報動作 - 透過 Telegram Bot API 發送警報訊息
/// 設定步驟:
/// 1. 與 @BotFather 對話建立 Bot，取得 Bot Token
/// 2. 與 Bot 對話後，訪問 https://api.telegram.org/bot{TOKEN}/getUpdates 取得 Chat ID
/// </summary>
public class TelegramAlertAction : IAlertAction
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private DateTime _lastExecutionTime = DateTime.MinValue;

    private const int CooldownSeconds = 30;
    private const string TelegramApiBaseUrl = "https://api.telegram.org/bot";

    public string ActionName => "Telegram";
    public bool IsEnabled { get; set; }

    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool DebugMode { get; set; } = false;

    public TelegramAlertAction(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        IsEnabled = settings.TelegramAlertEnabled;
        BotToken = settings.TelegramBotToken;
        ChatId = settings.TelegramChatId;
        DebugMode = settings.AlertActionsDebugMode;
    }

    public async Task ExecuteAsync(string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(ChatId))
        {
            Log.Warning("[TelegramAlertAction] Bot Token 或 Chat ID 未設定，無法發送訊息");
            return;
        }

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            Log.Debug("[TelegramAlertAction] 冷卻中，跳過發送 (剩餘 {Remaining:F0} 秒)",
                CooldownSeconds - elapsed.TotalSeconds);
            return;
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[TelegramAlertAction][DEBUG MODE] 將發送訊息: {Message}", message);
            _lastExecutionTime = DateTime.Now;
            return;
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            var formattedMessage = FormatMessage(message);
            Log.Information("[TelegramAlertAction] 發送 Telegram 訊息...");

            var success = await SendMessageAsync(formattedMessage);

            if (success)
            {
                Log.Information("[TelegramAlertAction] Telegram 訊息發送成功");
            }
            else
            {
                Log.Warning("[TelegramAlertAction] Telegram 訊息發送失敗");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TelegramAlertAction] 發送 Telegram 訊息時發生例外");
        }
    }

    private async Task<bool> SendMessageAsync(string text)
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
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            Log.Warning("[TelegramAlertAction] API 回應錯誤: {StatusCode} - {Body}",
                response.StatusCode, responseBody);
            return false;
        }
        catch (TaskCanceledException)
        {
            Log.Warning("[TelegramAlertAction] 請求逾時");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "[TelegramAlertAction] 網路請求失敗");
            return false;
        }
    }

    /// <summary>
    /// 格式化警報訊息為 Telegram HTML 格式
    /// </summary>
    private static string FormatMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return $"<b>16pin-vmon Alert</b>\n\n" +
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

    public void ShowConfigurationUI()
    {
        // T4-4: 將在 Alert Action Configuration UI 中實作
        Log.Debug("[TelegramAlertAction] 設定 UI 尚未實作");
    }
}
