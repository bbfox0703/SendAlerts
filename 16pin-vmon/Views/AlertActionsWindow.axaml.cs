using Avalonia.Controls;
using _16pin_vmon.ViewModels;

namespace _16pin_vmon.Views;

/// <summary>
/// TB1-1: 警報動作管理視窗
/// </summary>
public partial class AlertActionsWindow : Window
{
    public AlertActionsWindow()
    {
        InitializeComponent();

        var viewModel = new AlertActionsViewModel();
        DataContext = viewModel;

        // 訂閱關閉請求
        viewModel.CloseRequested += () => Close();
    }
}
