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
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTime _lastExecutionTime = DateTime.MinValue;

    private const int DefaultCooldownSeconds = AppConstants.DefaultCooldownSeconds;

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

    public async Task<AlertActionExecuteResult> ExecuteAsync(string message)
    {
        if (!IsEnabled)
            return AlertActionExecuteResult.Skipped("動作已停用");

        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            Log.Warning("[DiscordWebhookAlertAction] Webhook URL 未設定，無法發送訊息");
            return AlertActionExecuteResult.Fail("Webhook URL 未設定");
        }

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            var msg = $"冷卻中，跳過發送 (剩餘 {CooldownSeconds - elapsed.TotalSeconds:F0} 秒)";
            Log.Debug("[DiscordWebhookAlertAction] {Message}", msg);
            return AlertActionExecuteResult.Skipped(msg);
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[DiscordWebhookAlertAction][DEBUG MODE] 將發送訊息: {Message}", message);
            _lastExecutionTime = DateTime.Now;
            return AlertActionExecuteResult.Ok("[DEBUG] 模擬發送成功");
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            var formattedMessage = FormatMessage(message);
            Log.Information("[DiscordWebhookAlertAction] 發送 Discord Webhook 訊息...");

            return await SendMessageAsync(formattedMessage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DiscordWebhookAlertAction] 發送 Discord Webhook 訊息時發生例外");
            return AlertActionExecuteResult.Fail($"例外: {ex.Message}");
        }
    }

    private async Task<AlertActionExecuteResult> SendMessageAsync(string text)
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

            var response = await SharedHttpClient.PostAsync(WebhookUrl, content);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                Log.Information("[DiscordWebhookAlertAction] Discord Webhook 訊息發送成功");
                return AlertActionExecuteResult.Ok($"HTTP {statusCode} OK");
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            // 嘗試解析 Discord API 錯誤訊息
            var errorDetail = responseBody;
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("message", out var msg))
                    errorDetail = msg.GetString() ?? responseBody;
            }
            catch { /* 無法解析就用原始 body */ }

            Log.Warning("[DiscordWebhookAlertAction] API 回應錯誤: {StatusCode} - {Body}",
                statusCode, responseBody);
            return AlertActionExecuteResult.Fail($"HTTP {statusCode}: {errorDetail}", statusCode);
        }
        catch (TaskCanceledException)
        {
            Log.Warning("[DiscordWebhookAlertAction] 請求逾時");
            return AlertActionExecuteResult.Fail("請求逾時 (10 秒)");
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "[DiscordWebhookAlertAction] 網路請求失敗");
            return AlertActionExecuteResult.Fail($"網路錯誤: {ex.Message}");
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
