using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using _16pin_vmon.Services;
using Serilog;
using System;

namespace _16pin_vmon.ViewModels;

/// <summary>
/// T3-4: 設定視窗 ViewModel
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly AppSettings _settings;

    // --- Alert Thresholds ---
    [ObservableProperty] private float _voltageThreshold;
    [ObservableProperty] private float _temperatureThreshold;
    [ObservableProperty] private int _alertWindowSeconds;
    [ObservableProperty] private int _alertTriggerCount;

    // --- Sampling ---
    [ObservableProperty] private int _samplingIntervalSeconds;

    // --- Alert Actions (T4-4) ---
    [ObservableProperty] private bool _alertActionsDebugMode;

    // CommandLine
    [ObservableProperty] private bool _commandLineAlertEnabled;
    [ObservableProperty] private string _commandLineAlertCommand = string.Empty;
    [ObservableProperty] private int _commandLineAlertCooldownSeconds;

    // Telegram
    [ObservableProperty] private bool _telegramAlertEnabled;
    [ObservableProperty] private string _telegramBotToken = string.Empty;
    [ObservableProperty] private string _telegramChatId = string.Empty;

    // LINE Notify
    [ObservableProperty] private bool _lineNotifyAlertEnabled;
    [ObservableProperty] private string _lineNotifyAccessToken = string.Empty;

    // --- State ---
    [ObservableProperty] private bool _hasChanges;

    public event Action? OnSaved;
    public event Action? OnCancelled;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Load();

        // Load current settings
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        VoltageThreshold = _settings.VoltageThreshold;
        TemperatureThreshold = _settings.TemperatureThreshold;
        AlertWindowSeconds = _settings.AlertWindowSeconds;
        AlertTriggerCount = _settings.AlertTriggerCount;
        SamplingIntervalSeconds = _settings.SamplingIntervalSeconds;

        // T4-4: Alert Actions
        AlertActionsDebugMode = _settings.AlertActionsDebugMode;
        CommandLineAlertEnabled = _settings.CommandLineAlertEnabled;
        CommandLineAlertCommand = _settings.CommandLineAlertCommand;
        CommandLineAlertCooldownSeconds = _settings.CommandLineAlertCooldownSeconds;
        TelegramAlertEnabled = _settings.TelegramAlertEnabled;
        TelegramBotToken = _settings.TelegramBotToken;
        TelegramChatId = _settings.TelegramChatId;
        LineNotifyAlertEnabled = _settings.LineNotifyAlertEnabled;
        LineNotifyAccessToken = _settings.LineNotifyAccessToken;

        HasChanges = false;
    }

    partial void OnVoltageThresholdChanged(float value) => HasChanges = true;
    partial void OnTemperatureThresholdChanged(float value) => HasChanges = true;
    partial void OnAlertWindowSecondsChanged(int value) => HasChanges = true;
    partial void OnAlertTriggerCountChanged(int value) => HasChanges = true;
    partial void OnSamplingIntervalSecondsChanged(int value) => HasChanges = true;

    // T4-4: Alert Actions change tracking
    partial void OnAlertActionsDebugModeChanged(bool value) => HasChanges = true;
    partial void OnCommandLineAlertEnabledChanged(bool value) => HasChanges = true;
    partial void OnCommandLineAlertCommandChanged(string value) => HasChanges = true;
    partial void OnCommandLineAlertCooldownSecondsChanged(int value) => HasChanges = true;
    partial void OnTelegramAlertEnabledChanged(bool value) => HasChanges = true;
    partial void OnTelegramBotTokenChanged(string value) => HasChanges = true;
    partial void OnTelegramChatIdChanged(string value) => HasChanges = true;
    partial void OnLineNotifyAlertEnabledChanged(bool value) => HasChanges = true;
    partial void OnLineNotifyAccessTokenChanged(string value) => HasChanges = true;

    [RelayCommand]
    private void Save()
    {
        // Validate
        if (VoltageThreshold < 10.0f || VoltageThreshold > 13.0f)
        {
            Log.Warning("電壓門檻值超出合理範圍: {Value}", VoltageThreshold);
            VoltageThreshold = Math.Clamp(VoltageThreshold, 10.0f, 13.0f);
        }

        if (TemperatureThreshold < 50.0f || TemperatureThreshold > 100.0f)
        {
            Log.Warning("溫度門檻值超出合理範圍: {Value}", TemperatureThreshold);
            TemperatureThreshold = Math.Clamp(TemperatureThreshold, 50.0f, 100.0f);
        }

        if (AlertWindowSeconds < 1 || AlertWindowSeconds > 30)
        {
            AlertWindowSeconds = Math.Clamp(AlertWindowSeconds, 1, 30);
        }

        if (AlertTriggerCount < 1 || AlertTriggerCount > AlertWindowSeconds)
        {
            AlertTriggerCount = Math.Clamp(AlertTriggerCount, 1, AlertWindowSeconds);
        }

        if (SamplingIntervalSeconds < 1 || SamplingIntervalSeconds > 10)
        {
            SamplingIntervalSeconds = Math.Clamp(SamplingIntervalSeconds, 1, 10);
        }

        // Validate alert action settings
        if (CommandLineAlertCooldownSeconds < 5)
        {
            CommandLineAlertCooldownSeconds = 5;
        }

        // Save to settings
        _settings.VoltageThreshold = VoltageThreshold;
        _settings.TemperatureThreshold = TemperatureThreshold;
        _settings.AlertWindowSeconds = AlertWindowSeconds;
        _settings.AlertTriggerCount = AlertTriggerCount;
        _settings.SamplingIntervalSeconds = SamplingIntervalSeconds;

        // T4-4: Alert Actions
        _settings.AlertActionsDebugMode = AlertActionsDebugMode;
        _settings.CommandLineAlertEnabled = CommandLineAlertEnabled;
        _settings.CommandLineAlertCommand = CommandLineAlertCommand;
        _settings.CommandLineAlertCooldownSeconds = CommandLineAlertCooldownSeconds;
        _settings.TelegramAlertEnabled = TelegramAlertEnabled;
        _settings.TelegramBotToken = TelegramBotToken;
        _settings.TelegramChatId = TelegramChatId;
        _settings.LineNotifyAlertEnabled = LineNotifyAlertEnabled;
        _settings.LineNotifyAccessToken = LineNotifyAccessToken;

        _settingsService.Save(_settings);
        HasChanges = false;

        Log.Information(
            "設定已儲存 | 電壓門檻: {V}V | 溫度門檻: {T}°C | 判定: {W}秒/{C}次 | 取樣: {S}秒",
            VoltageThreshold, TemperatureThreshold, AlertWindowSeconds, AlertTriggerCount, SamplingIntervalSeconds);

        Log.Information(
            "警報動作設定 | Debug: {Debug} | CommandLine: {Cmd} | Telegram: {Tg} | LINE: {Line}",
            AlertActionsDebugMode, CommandLineAlertEnabled, TelegramAlertEnabled, LineNotifyAlertEnabled);

        OnSaved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings();
        OnCancelled?.Invoke();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        VoltageThreshold = 11.8f;
        TemperatureThreshold = 88.0f;
        AlertWindowSeconds = 3;
        AlertTriggerCount = 2;
        SamplingIntervalSeconds = 1;

        // T4-4: Alert Actions defaults
        AlertActionsDebugMode = false;
        CommandLineAlertEnabled = false;
        CommandLineAlertCommand = string.Empty;
        CommandLineAlertCooldownSeconds = 30;
        TelegramAlertEnabled = false;
        TelegramBotToken = string.Empty;
        TelegramChatId = string.Empty;
        LineNotifyAlertEnabled = false;
        LineNotifyAccessToken = string.Empty;

        HasChanges = true;
    }
}
