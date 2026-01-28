using Avalonia.Controls;
using SendAlerts.ViewModels;

namespace SendAlerts.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.OnSaved += () => Close(true);
        viewModel.OnCancelled += () => Close(false);
    }
}
