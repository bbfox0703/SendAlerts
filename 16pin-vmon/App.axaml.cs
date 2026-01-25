using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using _16pin_vmon.ViewModels;
using _16pin_vmon.Views;
using _16pin_vmon.Implementations;
using _16pin_vmon.Core.Interfaces;
using _16pin_vmon.Services;
using Serilog;

namespace _16pin_vmon;

public partial class App : Application
{
    private ISettingsService? _settingsService;
    private AppSettings? _settings;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // T3-1/T3-2: Initialize settings service
            _settingsService = new JsonSettingsService();
            _settings = _settingsService.Load();

            // T0-2: Check if disclaimer was already accepted
            if (_settings.DisclaimerAccepted)
            {
                Log.Information("免責聲明已於 {AcceptedAt} 確認，跳過顯示", _settings.DisclaimerAcceptedAt);
                ShowMainWindow(desktop);
            }
            else
            {
                // T0-1: Show disclaimer window first
                ShowDisclaimerWindow(desktop);
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            // For mobile/browser - skip disclaimer for now, use demo provider
            var gpuProvider = new DemoGpuProvider();
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel(gpuProvider)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowDisclaimerWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var disclaimerVm = new DisclaimerViewModel();
        var disclaimerWindow = new DisclaimerWindow(disclaimerVm);

        disclaimerWindow.Closed += (_, _) =>
        {
            if (disclaimerWindow.IsAccepted)
            {
                // T0-2: Save disclaimer acceptance
                if (_settings != null && _settingsService != null)
                {
                    _settings.DisclaimerAccepted = true;
                    _settings.DisclaimerAcceptedAt = DateTime.Now;
                    _settingsService.Save(_settings);
                    Log.Information("使用者已確認免責聲明");
                }

                ShowMainWindow(desktop);
            }
            else
            {
                Log.Information("使用者拒絕免責聲明，應用程式結束");
                desktop.Shutdown();
            }
        };

        desktop.MainWindow = disclaimerWindow;
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var gpuProvider = CreateGpuProvider();
        desktop.MainWindow = new MainWindow
        {
            DataContext = new MainViewModel(gpuProvider)
        };
        desktop.MainWindow.Show();
    }

    /// <summary>
    /// Creates the appropriate IGpuProvider based on platform.
    /// TODO (T1-1): Replace with proper DI container setup.
    /// </summary>
    private static IGpuProvider CreateGpuProvider()
    {
        // For now, use DemoGpuProvider
        // In the future, this will check RuntimeInformation and create NvmlWindowsProvider on Windows
        return new DemoGpuProvider();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
