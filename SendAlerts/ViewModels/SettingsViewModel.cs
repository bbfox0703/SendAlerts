using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Interfaces;
using SendAlerts.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SendAlerts.ViewModels;

/// <summary>
/// T3-4: 設定視窗 ViewModel (簡化版 - Alert Center 模式)
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    #region Localized Strings
    public string Loc_Title => _loc["Settings_Title"];
    public string Loc_SamplingInterval => _loc["Settings_SamplingInterval"];
    public string Loc_SamplingIntervalDesc => _loc["Settings_SamplingInterval_Desc"];
    public string Loc_HttpApiEnable => _loc["Settings_HttpApi_Enable"];
    public string Loc_HttpApiDesc => _loc["Settings_HttpApi_Desc"];
    public string Loc_HttpApiPort => _loc["Settings_HttpApi_Port"];
    public string Loc_HttpApiApiKey => _loc["Settings_HttpApi_ApiKey"];
    public string Loc_HttpApiApiKeyPlaceholder => _loc["Settings_HttpApi_ApiKey_Placeholder"];
    public string Loc_HttpApiRestartRequired => _loc["Settings_HttpApi_RestartRequired"];
    public string Loc_HttpApiExampleCommands => _loc["Settings_HttpApi_ExampleCommands"];
    public string Loc_HttpApiCopyPowerShell => _loc["Settings_HttpApi_CopyPowerShell"];
    public string Loc_AlertCenter => _loc["Settings_AlertCenter"];
    public string Loc_AlertCenterDesc => _loc["Settings_AlertCenter_Desc"];
    public string Loc_Language => _loc["Settings_Language"];
    public string Loc_LanguageDesc => _loc["Settings_Language_Desc"];
    public string Loc_Save => _loc["Save"];
    public string Loc_Cancel => _loc["Cancel"];
    public string Loc_Generate => _loc["Generate"];
    public string Loc_Copy => _loc["Copy"];
    public string Loc_AlertActions => _loc["Main_AlertActions"];
    public string Loc_AlertGroups => _loc["Main_AlertGroups"];
    public string Loc_Startup => _loc["Settings_Startup"];
    public string Loc_StartupEnable => _loc["Settings_Startup_Enable"];
    public string Loc_StartupDesc => _loc["Settings_Startup_Desc"];
    public string Loc_StartupNotSupported => _loc["Settings_Startup_NotSupported"];
    public string Loc_Watchdog => _loc["Settings_Watchdog"];
    public string Loc_WatchdogEnable => _loc["Settings_Watchdog_Enable"];
    public string Loc_WatchdogShutdownOnExit => _loc["Settings_Watchdog_ShutdownOnExit"];
    public string Loc_WatchdogDesc => _loc["Settings_Watchdog_Desc"];
    #endregion

    // --- Sampling ---
    [ObservableProperty] private int _samplingIntervalSeconds;

    // --- HTTP API ---
    [ObservableProperty] private bool _httpApiEnabled;
    [ObservableProperty] private int _httpApiPort;
    [ObservableProperty] private string _httpApiKey = string.Empty;

    // --- Startup ---
    [ObservableProperty] private bool _startupEnabled;

    // --- Watchdog ---
    [ObservableProperty] private bool _watchdogEnabled;
    [ObservableProperty] private bool _shutdownWatchdogOnExit;

    /// <summary>
    /// 平台是否支援自動啟動
    /// </summary>
    public bool IsStartupSupported => ServiceLocator.StartupManager?.IsSupported ?? false;

    // --- Language ---
    [ObservableProperty] private LanguageOption? _selectedLanguage;

    /// <summary>
    /// 可選的語系清單
    /// </summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.SupportedLanguages;

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

        // 載入啟動設定 (透過抽象層)
        StartupEnabled = ServiceLocator.StartupManager?.IsStartupEnabled() ?? false;

        // 載入 Watchdog 設定
        WatchdogEnabled = _settings.WatchdogEnabled;
        ShutdownWatchdogOnExit = _settings.ShutdownWatchdogOnExit;

        // 載入語系設定
        var langCode = _settings.Language ?? "auto";
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == langCode)
                           ?? AvailableLanguages.First();

        HasChanges = false;
    }

    partial void OnStartupEnabledChanged(bool value) => HasChanges = true;
    partial void OnSamplingIntervalSecondsChanged(int value) => HasChanges = true;
    partial void OnHttpApiEnabledChanged(bool value) => HasChanges = true;
    partial void OnHttpApiPortChanged(int value) => HasChanges = true;
    partial void OnHttpApiKeyChanged(string value)
    {
        HasChanges = true;
        OnPropertyChanged(nameof(HasApiKey));
    }
    partial void OnSelectedLanguageChanged(LanguageOption? value) => HasChanges = true;
    partial void OnWatchdogEnabledChanged(bool value) => HasChanges = true;
    partial void OnShutdownWatchdogOnExitChanged(bool value) => HasChanges = true;

    /// <summary>
    /// 產生新的 API Key
    /// </summary>
    [RelayCommand]
    private void GenerateApiKey()
    {
        HttpApiKey = GenerateRandomKey();
        StatusMessage = _loc["Settings_ApiKeyGenerated"];
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
            StatusMessage = _loc["Settings_ApiKeyCopied"];
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
        StatusMessage = _loc["Settings_ExampleCopied"];
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
        if (SamplingIntervalSeconds < AppConstants.SamplingIntervalMin || SamplingIntervalSeconds > AppConstants.SamplingIntervalMax)
        {
            SamplingIntervalSeconds = Math.Clamp(SamplingIntervalSeconds, AppConstants.SamplingIntervalMin, AppConstants.SamplingIntervalMax);
        }

        if (HttpApiPort < 1024 || HttpApiPort > 65535)
        {
            HttpApiPort = Math.Clamp(HttpApiPort, 1024, 65535);
        }

        // 檢查 HTTP API 設定是否變更
        var httpSettingsChanged = _settings.HttpApiEnabled != HttpApiEnabled ||
                                  _settings.HttpApiPort != HttpApiPort ||
                                  _settings.HttpApiKey != HttpApiKey;

        // 檢查語系設定是否變更
        var newLangCode = SelectedLanguage?.Code == "auto" ? null : SelectedLanguage?.Code;
        var languageChanged = _settings.Language != newLangCode;

        // 儲存啟動設定 (透過抽象層，不存入 AppSettings)
        var startupMgr = ServiceLocator.StartupManager;
        if (startupMgr != null && startupMgr.IsSupported)
        {
            var currentEnabled = startupMgr.IsStartupEnabled();
            if (StartupEnabled && !currentEnabled)
                startupMgr.EnableStartup();
            else if (!StartupEnabled && currentEnabled)
                startupMgr.DisableStartup();
        }

        // Save to settings
        _settings.SamplingIntervalSeconds = SamplingIntervalSeconds;
        _settings.HttpApiEnabled = HttpApiEnabled;
        _settings.HttpApiPort = HttpApiPort;
        _settings.HttpApiKey = HttpApiKey;
        _settings.WatchdogEnabled = WatchdogEnabled;
        _settings.ShutdownWatchdogOnExit = ShutdownWatchdogOnExit;
        _settings.Language = newLangCode;

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
