using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SendAlerts.Views;

/// <summary>
/// CLI 指令顯示對話框
/// </summary>
public partial class CliCommandDialog : Window
{
    public CliCommandDialog()
    {
        InitializeComponent();
    }

    public CliCommandDialog(string title, string content) : this()
    {
        HeaderText.Text = title;
        ContentTextBox.Text = content;
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && ContentTextBox.Text != null)
        {
            await clipboard.SetTextAsync(ContentTextBox.Text);
            CopyButton.Content = "Copied!";

            // 2 秒後恢復按鈕文字
            await System.Threading.Tasks.Task.Delay(2000);
            CopyButton.Content = "Copy to Clipboard";
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
