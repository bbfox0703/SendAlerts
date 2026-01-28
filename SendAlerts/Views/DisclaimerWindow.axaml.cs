using Avalonia.Controls;
using SendAlerts.ViewModels;

namespace SendAlerts.Views;

public partial class DisclaimerWindow : Window
{
    public bool IsAccepted { get; private set; }

    public DisclaimerWindow()
    {
        InitializeComponent();
    }

    public DisclaimerWindow(DisclaimerViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.OnAccepted += () =>
        {
            IsAccepted = true;
            Close();
        };

        viewModel.OnDeclined += () =>
        {
            IsAccepted = false;
            Close();
        };
    }
}
