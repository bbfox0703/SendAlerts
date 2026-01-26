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
        HasChanges = false;
    }

    partial void OnVoltageThresholdChanged(float value) => HasChanges = true;
    partial void OnTemperatureThresholdChanged(float value) => HasChanges = true;
    partial void OnAlertWindowSecondsChanged(int value) => HasChanges = true;
    partial void OnAlertTriggerCountChanged(int value) => HasChanges = true;
    partial void OnSamplingIntervalSecondsChanged(int value) => HasChanges = true;

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

        // Save to settings
        _settings.VoltageThreshold = VoltageThreshold;
        _settings.TemperatureThreshold = TemperatureThreshold;
        _settings.AlertWindowSeconds = AlertWindowSeconds;
        _settings.AlertTriggerCount = AlertTriggerCount;
        _settings.SamplingIntervalSeconds = SamplingIntervalSeconds;

        _settingsService.Save(_settings);
        HasChanges = false;

        Log.Information(
            "設定已儲存 | 電壓門檻: {V}V | 溫度門檻: {T}°C | 判定: {W}秒/{C}次 | 取樣: {S}秒",
            VoltageThreshold, TemperatureThreshold, AlertWindowSeconds, AlertTriggerCount, SamplingIntervalSeconds);

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
        HasChanges = true;
    }
}
