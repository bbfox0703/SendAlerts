using Avalonia.Controls;
using Avalonia.Interactivity;
using _16pin_vmon.ViewModels;
using _16pin_vmon.Services;

namespace _16pin_vmon.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsService = ServiceLocator.SettingsService ?? new JsonSettingsService();
        var viewModel = new SettingsViewModel(settingsService);
        var settingsWindow = new SettingsWindow(viewModel);

        // Get the parent window
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow != null)
        {
            await settingsWindow.ShowDialog(parentWindow);
        }
    }

    private async void OnAlertActionsClick(object? sender, RoutedEventArgs e)
    {
        var alertActionsWindow = new AlertActionsWindow();

        // Get the parent window
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow != null)
        {
            await alertActionsWindow.ShowDialog(parentWindow);
        }
    }

    private async void OnAlertGroupsClick(object? sender, RoutedEventArgs e)
    {
        var alertGroupsWindow = new AlertGroupsWindow();

        // Get the parent window
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow != null)
        {
            await alertGroupsWindow.ShowDialog(parentWindow);
        }
    }

    // TC1-1: 移除 OnTestAlertClick - 警報功能已移至 Alert Center
    // 測試警報請透過 AlertGroupsWindow 的測試按鈕或 Named Pipe 發送
}
