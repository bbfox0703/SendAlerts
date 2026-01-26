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

    private void OnTestAlertClick(object? sender, RoutedEventArgs e)
    {
        // For testing: manually trigger alert state
        if (DataContext is MainViewModel vm)
        {
            // This is just for visual testing - doesn't affect actual alert logic
            Serilog.Log.Information("[TEST] 使用者手動觸發測試警報");
        }
    }
}
