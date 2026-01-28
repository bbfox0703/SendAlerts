using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SendAlerts.Core.Interfaces;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// Discord Webhook 警報動作 - 透過 Discord Webhook API 發送警報訊息
/// 設定步驟:
/// 1. 在 Discord 伺服器的頻道設定中，選擇「整合」→「Webhook」
/// 2. 點選「新增 Webhook」，設定名稱與頭像
/// 3. 複製 Webhook URL
/// </summary>
public class DiscordWebhookAlertAction : IAlertAction
{
    private readonly HttpClient _httpClient;
    private DateTime _lastExecutionTime = DateTime.MinValue;

    private const int DefaultCooldownSeconds = 30;

    public string InstanceId { get; set; } = "Discord_Default";
    public AlertActionType ActionType => AlertActionType.Discord;
    public string DisplayName => string.IsNullOrWhiteSpace(InstanceId) ? "Discord" : InstanceId;
    public bool IsEnabled { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;
    public string? Username { get; set; }
    public bool DebugMode { get; set; } = false;
    public int CooldownSeconds { get; set; } = DefaultCooldownSeconds;

    /// <summary>
    /// 建構子 - 直接指定參數 (多實例支援)
    /// </summary>
    public DiscordWebhookAlertAction(string instanceId, string webhookUrl, string? username = null, int cooldownSeconds = DefaultCooldownSeconds, bool debugMode = false)
    {
        InstanceId = instanceId;
        WebhookUrl = webhookUrl;
        Username = username;
        CooldownSeconds = cooldownSeconds;
        DebugMode = debugMode;
        IsEnabled = true;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// 驗證設定是否有效
    /// </summary>
    public AlertActionValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
            return AlertActionValidationResult.Invalid("Webhook URL 不可為空");

        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return AlertActionValidationResult.Invalid("Webhook URL 格式無效");

        if (!WebhookUrl.Contains("discord.com/api/webhooks/") && !WebhookUrl.Contains("discordapp.com/api/webhooks/"))
            return AlertActionValidationResult.Invalid("這不是有效的 Discord Webhook URL");

        return AlertActionValidationResult.Valid();
    }

    public async Task ExecuteAsync(string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            Log.Warning("[DiscordWebhookAlertAction] Webhook URL 未設定，無法發送訊息");
            return;
        }

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            Log.Debug("[DiscordWebhookAlertAction] 冷卻中，跳過發送 (剩餘 {Remaining:F0} 秒)",
                CooldownSeconds - elapsed.TotalSeconds);
            return;
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[DiscordWebhookAlertAction][DEBUG MODE] 將發送訊息: {Message}", message);
            _lastExecutionTime = DateTime.Now;
            return;
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            var formattedMessage = FormatMessage(message);
            Log.Information("[DiscordWebhookAlertAction] 發送 Discord Webhook 訊息...");

            var success = await SendMessageAsync(formattedMessage);

            if (success)
            {
                Log.Information("[DiscordWebhookAlertAction] Discord Webhook 訊息發送成功");
            }
            else
            {
                Log.Warning("[DiscordWebhookAlertAction] Discord Webhook 訊息發送失敗");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DiscordWebhookAlertAction] 發送 Discord Webhook 訊息時發生例外");
        }
    }

    private async Task<bool> SendMessageAsync(string text)
    {
        try
        {
            var payload = new
            {
                content = text,
                username = string.IsNullOrWhiteSpace(Username) ? "SendAlerts" : Username
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(WebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            Log.Warning("[DiscordWebhookAlertAction] API 回應錯誤: {StatusCode} - {Body}",
                response.StatusCode, responseBody);
            return false;
        }
        catch (TaskCanceledException)
        {
            Log.Warning("[DiscordWebhookAlertAction] 請求逾時");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "[DiscordWebhookAlertAction] 網路請求失敗");
            return false;
        }
    }

    /// <summary>
    /// 格式化警報訊息
    /// </summary>
    private static string FormatMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return $"**SendAlerts Alert**\n\n{message}\n\n_{timestamp}_";
    }
}
