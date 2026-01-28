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
        SamplingIntervalSeconds = _settings.SamplingIntervalSeconds;
        HasChanges = false;
    }

    partial void OnSamplingIntervalSecondsChanged(int value) => HasChanges = true;

    [RelayCommand]
    private void Save()
    {
        // Validate
        if (SamplingIntervalSeconds < 1 || SamplingIntervalSeconds > 10)
        {
            SamplingIntervalSeconds = Math.Clamp(SamplingIntervalSeconds, 1, 10);
        }

        // Save to settings
        _settings.SamplingIntervalSeconds = SamplingIntervalSeconds;

        _settingsService.Save(_settings);
        HasChanges = false;

        Log.Information("設定已儲存 | 取樣間隔: {S} 秒", SamplingIntervalSeconds);

        OnSaved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings();
        OnCancelled?.Invoke();
    }
}
