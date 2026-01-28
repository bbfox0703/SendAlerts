using Avalonia.Controls;
using Avalonia.Interactivity;
using SendAlerts.Services;

namespace SendAlerts.Views;

/// <summary>
/// CLI 指令顯示對話框
/// </summary>
public partial class CliCommandDialog : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public CliCommandDialog()
    {
        InitializeComponent();
        InitializeLocalization();
    }

    public CliCommandDialog(string title, string content) : this()
    {
        HeaderText.Text = title;
        ContentTextBox.Text = content;
    }

    private void InitializeLocalization()
    {
        Title = _loc["CliDialog_Title"];
        CopyButton.Content = _loc["CliDialog_CopyToClipboard"];
        CloseButton.Content = _loc["Close"];
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && ContentTextBox.Text != null)
        {
            await clipboard.SetTextAsync(ContentTextBox.Text);
            CopyButton.Content = _loc["CliDialog_Copied"];

            // 2 秒後恢復按鈕文字
            await System.Threading.Tasks.Task.Delay(2000);
            CopyButton.Content = _loc["CliDialog_CopyToClipboard"];
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
