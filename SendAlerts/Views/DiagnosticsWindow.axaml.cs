using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SendAlerts.Services;

namespace SendAlerts.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();

        var reportBox = this.FindControl<TextBox>("ReportTextBox");
        if (reportBox is not null)
            reportBox.Text = DiagnosticsReport.Generate();
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        var reportBox = this.FindControl<TextBox>("ReportTextBox");
        if (reportBox?.Text is { } text && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
