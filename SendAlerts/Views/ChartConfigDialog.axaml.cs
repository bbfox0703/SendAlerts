using Avalonia.Controls;
using Avalonia.Interactivity;
using SendAlerts.Models;
using SendAlerts.ViewModels;

namespace SendAlerts.Views;

public partial class ChartConfigDialog : Window
{
    public ChartSlotConfig? Result => (DataContext as ChartConfigDialogViewModel)?.Result;

    public ChartConfigDialog()
    {
        InitializeComponent();
    }

    public ChartConfigDialog(ChartConfigDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChartConfigDialogViewModel vm && vm.TryBuildResult())
        {
            Close(true);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
