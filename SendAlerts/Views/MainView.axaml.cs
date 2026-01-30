using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SendAlerts.ViewModels;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsService = ServiceLocator.SettingsService ?? new JsonSettingsService();
            var viewModel = new SettingsViewModel(settingsService);
            var settingsWindow = new SettingsWindow(viewModel);

            // Get the parent window
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await settingsWindow.ShowDialog(parentWindow);

                // 設定儲存後，套用新的取樣間隔
                if (DataContext is MainViewModel mainVm)
                {
                    var settings = settingsService.Load();
                    mainVm.UpdateSamplingInterval(settings.SamplingIntervalSeconds);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟設定視窗失敗");
        }
    }

    private async void OnAlertActionsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var alertActionsWindow = new AlertActionsWindow();

            // Get the parent window
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await alertActionsWindow.ShowDialog(parentWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟 Alert Actions 視窗失敗");
        }
    }

    private async void OnAlertGroupsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var alertGroupsWindow = new AlertGroupsWindow();

            // Get the parent window
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await alertGroupsWindow.ShowDialog(parentWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainView] 開啟 Alert Groups 視窗失敗");
        }
    }

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        var logWindow = new LogWindow();
        logWindow.Show();
    }
}
