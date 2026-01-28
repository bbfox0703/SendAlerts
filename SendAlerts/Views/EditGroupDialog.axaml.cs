using Avalonia.Controls;
using SendAlerts.Models;
using SendAlerts.ViewModels;

namespace SendAlerts.Views;

/// <summary>
/// TB2-2/TB2-3: 新增/編輯 Group 對話框
/// </summary>
public partial class EditGroupDialog : Window
{
    private readonly EditGroupViewModel _viewModel;

    /// <summary>
    /// 新增模式
    /// </summary>
    public EditGroupDialog() : this(null) { }

    /// <summary>
    /// 編輯模式
    /// </summary>
    public EditGroupDialog(AlertGroup? existingGroup)
    {
        InitializeComponent();

        _viewModel = new EditGroupViewModel(existingGroup);
        DataContext = _viewModel;

        _viewModel.CloseRequested += () => Close(_viewModel.DialogResult);
    }

    /// <summary>
    /// 取得對話框結果
    /// </summary>
    public bool DialogResult => _viewModel.DialogResult;

    /// <summary>
    /// 取得建立的群組
    /// </summary>
    public AlertGroup? GetResult() => _viewModel.CreateGroup();
}
