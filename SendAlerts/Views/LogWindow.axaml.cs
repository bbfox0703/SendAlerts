using System.Collections.Specialized;
using Avalonia.Controls;
using SendAlerts.ViewModels;

namespace SendAlerts.Views;

public partial class LogWindow : Window
{
    private readonly LogViewModel _viewModel;

    public LogWindow()
    {
        InitializeComponent();
        _viewModel = new LogViewModel();
        DataContext = _viewModel;

        var listBox = this.FindControl<ListBox>("LogListBox");
        if (listBox != null)
        {
            ((INotifyCollectionChanged)_viewModel.LogEntries).CollectionChanged += (_, _) =>
            {
                if (_viewModel.AutoScroll && _viewModel.LogEntries.Count > 0)
                {
                    listBox.ScrollIntoView(_viewModel.LogEntries[^1]);
                }
            };
        }
    }
}
