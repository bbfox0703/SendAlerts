using Avalonia.Controls;
using SendAlerts.Models;
using SendAlerts.ViewModels;

namespace SendAlerts.Views;

/// <summary>
/// TB1-2/TB1-3: 新增/編輯 Action 對話框
/// </summary>
public partial class EditActionDialog : Window
{
    private readonly EditActionViewModel _viewModel;

    /// <summary>
    /// 新增模式
    /// </summary>
    public EditActionDialog() : this(null) { }

    /// <summary>
    /// 編輯模式
    /// </summary>
    public EditActionDialog(AlertActionConfig? existingConfig)
    {
        InitializeComponent();

        _viewModel = new EditActionViewModel(existingConfig);
        DataContext = _viewModel;

        _viewModel.CloseRequested += () => Close(_viewModel.DialogResult);
    }

    /// <summary>
    /// 取得對話框結果
    /// </summary>
    public bool DialogResult => _viewModel.DialogResult;

    /// <summary>
    /// 取得建立的設定
    /// </summary>
    public AlertActionConfig? GetResult() => _viewModel.CreateConfig();
}
