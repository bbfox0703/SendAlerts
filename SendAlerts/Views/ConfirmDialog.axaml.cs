using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SendAlerts.Views;

public partial class ConfirmDialog : Window
{
    public bool IsConfirmed { get; private set; }

    public ConfirmDialog() => InitializeComponent();

    public ConfirmDialog(string title, string message, string confirmText = "OK", string cancelText = "Cancel") : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close(false);
    }
}
