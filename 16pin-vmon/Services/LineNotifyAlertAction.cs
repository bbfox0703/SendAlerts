using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using _16pin_vmon.Core.Interfaces;
using Serilog;

namespace _16pin_vmon.Services;

/// <summary>
/// T4-3/TA2-1: LINE Notify 警報動作 - 透過 LINE Notify API 發送警報訊息
/// 設定步驟:
/// 1. 前往 https://notify-bot.line.me/
/// 2. 登入後點選「發行權杖」
/// 3. 選擇要接收通知的聊天室，取得 Access Token
/// </summary>
public class LineNotifyAlertAction : IAlertAction
{
    private readonly ISettingsService? _settingsService;
    private readonly HttpClient _httpClient;
    private DateTime _lastExecutionTime = DateTime.MinValue;

    private const int DefaultCooldownSeconds = 30;
    private const string LineNotifyApiUrl = "https://notify-api.line.me/api/notify";

    // TA2-1: 新增介面成員
    public string InstanceId { get; set; } = "LINE_Default";
    public AlertActionType ActionType => AlertActionType.LineNotify;
    public string DisplayName => string.IsNullOrWhiteSpace(InstanceId) ? "LINE Notify" : InstanceId;
    public bool IsEnabled { get; set; }

    public string AccessToken { get; set; } = string.Empty;
    public bool DebugMode { get; set; } = false;
    public int CooldownSeconds { get; set; } = DefaultCooldownSeconds;

    /// <summary>
    /// 建構子 - 從設定服務載入 (舊版相容)
    /// </summary>
    public LineNotifyAlertAction(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        LoadSettings();
    }

    /// <summary>
    /// 建構子 - 直接指定參數 (多實例支援)
    /// </summary>
    public LineNotifyAlertAction(string instanceId, string accessToken, int cooldownSeconds = DefaultCooldownSeconds, bool debugMode = false)
    {
        InstanceId = instanceId;
        AccessToken = accessToken;
        CooldownSeconds = cooldownSeconds;
        DebugMode = debugMode;
        IsEnabled = true;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    private void LoadSettings()
    {
        if (_settingsService == null) return;
        var settings = _settingsService.Load();
        IsEnabled = settings.LineNotifyAlertEnabled;
        AccessToken = settings.LineNotifyAccessToken;
        DebugMode = settings.AlertActionsDebugMode;
    }

    /// <summary>
    /// TA2-1: 驗證設定是否有效
    /// </summary>
    public AlertActionValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
            return AlertActionValidationResult.Invalid("Access Token 不可為空");

        return AlertActionValidationResult.Valid();
    }

    public async Task ExecuteAsync(string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            Log.Warning("[LineNotifyAlertAction] Access Token 未設定，無法發送訊息");
            return;
        }

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            Log.Debug("[LineNotifyAlertAction] 冷卻中，跳過發送 (剩餘 {Remaining:F0} 秒)",
                CooldownSeconds - elapsed.TotalSeconds);
            return;
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[LineNotifyAlertAction][DEBUG MODE] 將發送訊息: {Message}", message);
            _lastExecutionTime = DateTime.Now;
            return;
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            var formattedMessage = FormatMessage(message);
            Log.Information("[LineNotifyAlertAction] 發送 LINE Notify 訊息...");

            var success = await SendMessageAsync(formattedMessage);

            if (success)
            {
                Log.Information("[LineNotifyAlertAction] LINE Notify 訊息發送成功");
            }
            else
            {
                Log.Warning("[LineNotifyAlertAction] LINE Notify 訊息發送失敗");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LineNotifyAlertAction] 發送 LINE Notify 訊息時發生例外");
        }
    }

    private async Task<bool> SendMessageAsync(string text)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, LineNotifyApiUrl);
            request.Headers.Add("Authorization", $"Bearer {AccessToken}");

            var formData = new Dictionary<string, string>
            {
                { "message", text }
            };
            request.Content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            Log.Warning("[LineNotifyAlertAction] API 回應錯誤: {StatusCode} - {Body}",
                response.StatusCode, responseBody);
            return false;
        }
        catch (TaskCanceledException)
        {
            Log.Warning("[LineNotifyAlertAction] 請求逾時");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "[LineNotifyAlertAction] 網路請求失敗");
            return false;
        }
    }

    /// <summary>
    /// 格式化警報訊息
    /// </summary>
    private static string FormatMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return $"\n[16pin-vmon Alert]\n{message}\n{timestamp}";
    }
}
