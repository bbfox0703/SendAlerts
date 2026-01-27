using System.Threading.Tasks;
using Avalonia.Controls;
using _16pin_vmon.Models;
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

        // TB1-2: 訂閱新增對話框請求
        viewModel.AddActionRequested += OnAddActionRequested;

        // TB1-3: 訂閱編輯對話框請求
        viewModel.EditActionRequested += OnEditActionRequested;
    }

    /// <summary>
    /// TB1-2: 開啟新增對話框
    /// </summary>
    private async Task<AlertActionConfig?> OnAddActionRequested()
    {
        var dialog = new EditActionDialog();
        var result = await dialog.ShowDialog<bool?>(this);

        if (result == true)
        {
            return dialog.GetResult();
        }
        return null;
    }

    /// <summary>
    /// TB1-3: 開啟編輯對話框
    /// </summary>
    private async Task<AlertActionConfig?> OnEditActionRequested(AlertActionConfig existingConfig)
    {
        var dialog = new EditActionDialog(existingConfig);
        var result = await dialog.ShowDialog<bool?>(this);

        if (result == true)
        {
            return dialog.GetResult();
        }
        return null;
    }
}
