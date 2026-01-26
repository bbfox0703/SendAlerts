using Avalonia.Controls;
using _16pin_vmon.ViewModels;

namespace _16pin_vmon.Views;

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
