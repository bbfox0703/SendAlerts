using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using SendAlerts.Models;
using SendAlerts.ViewModels;
using Serilog;

namespace SendAlerts.Views;

/// <summary>
/// TB2-1: 警報群組管理視窗
/// </summary>
public partial class AlertGroupsWindow : Window
{
    public AlertGroupsWindow()
    {
        InitializeComponent();

        var viewModel = new AlertGroupsViewModel();
        DataContext = viewModel;

        // 訂閱關閉請求
        viewModel.CloseRequested += () => Close();

        // TB2-2: 訂閱新增對話框請求
        viewModel.AddGroupRequested += OnAddGroupRequested;

        // TB2-2: 訂閱編輯對話框請求
        viewModel.EditGroupRequested += OnEditGroupRequested;

        // 訂閱顯示 CLI 指令請求
        viewModel.ShowCliCommandRequested += OnShowCliCommandRequested;
    }

    /// <summary>
    /// TB2-2: 開啟新增對話框
    /// </summary>
    private async Task<AlertGroup?> OnAddGroupRequested()
    {
        var dialog = new EditGroupDialog();
        var result = await dialog.ShowDialog<bool?>(this);

        if (result == true)
        {
            return dialog.GetResult();
        }
        return null;
    }

    /// <summary>
    /// TB2-2: 開啟編輯對話框
    /// </summary>
    private async Task<AlertGroup?> OnEditGroupRequested(AlertGroup existingGroup)
    {
        var dialog = new EditGroupDialog(existingGroup);
        var result = await dialog.ShowDialog<bool?>(this);

        if (result == true)
        {
            return dialog.GetResult();
        }
        return null;
    }

    /// <summary>
    /// 顯示 CLI 指令對話框
    /// </summary>
    private async void OnShowCliCommandRequested(string title, string content)
    {
        try
        {
            var dialog = new CliCommandDialog(title, content);
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AlertGroupsWindow] 顯示 CLI 指令對話框失敗");
        }
    }
}
