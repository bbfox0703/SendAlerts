using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Services;
using Serilog;
using System;

namespace SendAlerts.ViewModels;

/// <summary>
/// T3-4: 設定視窗 ViewModel (簡化版 - Alert Center 模式)
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;

    // --- Sampling ---
    [ObservableProperty] private int _samplingIntervalSeconds;

    // --- HTTP API ---
    [ObservableProperty] private bool _httpApiEnabled;
    [ObservableProperty] private int _httpApiPort;
    [ObservableProperty] private string _httpApiKey = string.Empty;

    // --- State ---
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>
    /// API Key 是否已設定
    /// </summary>
    public bool HasApiKey => !string.IsNullOrEmpty(HttpApiKey);

    /// <summary>
    /// HTTP API 需要重啟才能生效
    /// </summary>
    public event Action? HttpApiSettingsChanged;

    public event Action? OnSaved;
    public event Action? OnCancelled;

    /// <summary>
    /// 複製 API Key 到剪貼簿的請求事件
    /// </summary>
    public event Action<string>? CopyToClipboardRequested;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Load();

        // Load current settings
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        SamplingIntervalSeconds = _settings.SamplingIntervalSeconds;
        HttpApiEnabled = _settings.HttpApiEnabled;
        HttpApiPort = _settings.HttpApiPort;
        HttpApiKey = _settings.HttpApiKey;
        HasChanges = false;
    }

    partial void OnSamplingIntervalSecondsChanged(int value) => HasChanges = true;
    partial void OnHttpApiEnabledChanged(bool value) => HasChanges = true;
    partial void OnHttpApiPortChanged(int value) => HasChanges = true;
    partial void OnHttpApiKeyChanged(string value)
    {
        HasChanges = true;
        OnPropertyChanged(nameof(HasApiKey));
    }

    /// <summary>
    /// 產生新的 API Key
    /// </summary>
    [RelayCommand]
    private void GenerateApiKey()
    {
        HttpApiKey = GenerateRandomKey();
        StatusMessage = "New API Key generated";
    }

    /// <summary>
    /// 複製 API Key 到剪貼簿
    /// </summary>
    [RelayCommand]
    private void CopyApiKey()
    {
        if (!string.IsNullOrEmpty(HttpApiKey))
        {
            CopyToClipboardRequested?.Invoke(HttpApiKey);
            StatusMessage = "API Key copied to clipboard";
        }
    }

    /// <summary>
    /// 複製範例指令到剪貼簿
    /// </summary>
    [RelayCommand]
    private void CopyExampleCommand()
    {
        var example = GetPowerShellExample();
        CopyToClipboardRequested?.Invoke(example);
        StatusMessage = "Example command copied to clipboard";
    }

    /// <summary>
    /// 取得 PowerShell 範例指令
    /// </summary>
    public string GetPowerShellExample()
    {
        return $@"# PowerShell Example
$headers = @{{ ""X-API-Key"" = ""{HttpApiKey}"" }}
$body = @{{ groupName = ""Critical""; message = ""Your alert message"" }} | ConvertTo-Json
Invoke-RestMethod -Uri ""http://YOUR_IP:{HttpApiPort}/api/send"" -Method Post -Headers $headers -Body $body -ContentType ""application/json""";
    }

    /// <summary>
    /// 取得 Python 範例指令
    /// </summary>
    public string GetPythonExample()
    {
        return $@"# Python Example
import requests

url = ""http://YOUR_IP:{HttpApiPort}/api/send""
headers = {{""X-API-Key"": ""{HttpApiKey}""}}
data = {{""groupName"": ""Critical"", ""message"": ""Your alert message""}}

response = requests.post(url, json=data, headers=headers)
print(response.json())";
    }

    /// <summary>
    /// 產生隨機 API Key (32 字元)
    /// </summary>
    private static string GenerateRandomKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var key = new char[32];
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = chars[random.Next(chars.Length)];
        }
        return new string(key);
    }

    [RelayCommand]
    private void Save()
    {
        // Validate
        if (SamplingIntervalSeconds < 1 || SamplingIntervalSeconds > 10)
        {
            SamplingIntervalSeconds = Math.Clamp(SamplingIntervalSeconds, 1, 10);
        }

        if (HttpApiPort < 1024 || HttpApiPort > 65535)
        {
            HttpApiPort = Math.Clamp(HttpApiPort, 1024, 65535);
        }

        // 檢查 HTTP API 設定是否變更
        var httpSettingsChanged = _settings.HttpApiEnabled != HttpApiEnabled ||
                                  _settings.HttpApiPort != HttpApiPort ||
                                  _settings.HttpApiKey != HttpApiKey;

        // Save to settings
        _settings.SamplingIntervalSeconds = SamplingIntervalSeconds;
        _settings.HttpApiEnabled = HttpApiEnabled;
        _settings.HttpApiPort = HttpApiPort;
        _settings.HttpApiKey = HttpApiKey;

        _settingsService.Save(_settings);
        HasChanges = false;

        Log.Information("設定已儲存 | 取樣間隔: {S} 秒 | HTTP API: {Enabled}",
            SamplingIntervalSeconds, HttpApiEnabled ? "啟用" : "停用");

        if (httpSettingsChanged)
        {
            HttpApiSettingsChanged?.Invoke();
        }

        OnSaved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings();
        OnCancelled?.Invoke();
    }
}
