using Avalonia.Controls;
using Avalonia.Interactivity;
using SendAlerts.ViewModels;
using SendAlerts.Services;

namespace SendAlerts.Views;

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

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        var logWindow = new LogWindow();
        logWindow.Show();
    }
}
