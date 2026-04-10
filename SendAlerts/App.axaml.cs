using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using Avalonia.Markup.Xaml;
using SendAlerts.ViewModels;
using SendAlerts.Views;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Enable UI dispatching for log sink now that Avalonia is initialized
        InMemoryLogSink.Instance.MarkDispatcherReady();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 處理 OS 關機 / 登出時的 ShutdownRequested 事件
            desktop.ShutdownRequested += (_, e) =>
            {
                // 設定旗標讓 MainWindow.OnWindowClosing 不再攔截
                ServiceLocator.IsSessionEnding = true;
                Log.Information("[App] ShutdownRequested received");
            };

            // T1-1: Use ServiceLocator for settings (initialized by Platform Head)
            var settingsService = ServiceLocator.SettingsService ?? new JsonSettingsService();
            var settings = settingsService.Load();

            // T0-2: Check if disclaimer was already accepted
            if (settings.DisclaimerAccepted)
            {
                Log.Information("免責聲明已於 {AcceptedAt} 確認，跳過顯示", settings.DisclaimerAcceptedAt);
                ShowMainWindow(desktop);
            }
            else
            {
                // T0-1: Show disclaimer window first
                ShowDisclaimerWindow(desktop, settingsService, settings);
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel(true)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowDisclaimerWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        ISettingsService settingsService,
        AppSettings settings)
    {
        var disclaimerVm = new DisclaimerViewModel();
        var disclaimerWindow = new DisclaimerWindow(disclaimerVm);

        disclaimerWindow.Closed += (_, _) =>
        {
            if (disclaimerWindow.IsAccepted)
            {
                // T0-2: Save disclaimer acceptance
                settings.DisclaimerAccepted = true;
                settings.DisclaimerAcceptedAt = DateTime.Now;
                settingsService.Save(settings);
                Log.Information("使用者已確認免責聲明");

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
        desktop.MainWindow = new MainWindow
        {
            DataContext = new MainViewModel(true)
        };
        desktop.MainWindow.Show();
    }
}
