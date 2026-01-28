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

        // 處理複製到剪貼簿的請求
        viewModel.CopyToClipboardRequested += async (text) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
            }
        };
    }
}
