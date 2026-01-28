using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Models;
using SendAlerts.Services;

namespace SendAlerts.ViewModels;

/// <summary>
/// TB2-2/TB2-3: 新增/編輯 Group 對話框 ViewModel
/// </summary>
public partial class EditGroupViewModel : ViewModelBase
{
    private readonly AlertGroup? _existingGroup;
    private readonly AppSettings? _settings;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    #region Localized Strings
    public string Loc_GroupSettings => _loc["Settings"];
    public string Loc_Name => _loc["EditGroup_Name"];
    public string Loc_Description => _loc["EditGroup_Description"];
    public string Loc_Enabled => _loc["Enabled"];
    public string Loc_MessageTemplate => _loc["EditGroup_MessageTemplate"];
    public string Loc_SelectActions => _loc["EditGroup_SelectActions"];
    public string Loc_SelectAll => _loc["EditGroup_SelectAll"];
    public string Loc_SelectNone => _loc["EditGroup_SelectNone"];
    public string Loc_Cancel => _loc["Cancel"];
    public string Loc_OK => _loc["OK"];
    #endregion

    /// <summary>
    /// 是否為編輯模式
    /// </summary>
    public bool IsEditMode => _existingGroup != null;

    /// <summary>
    /// 對話框標題
    /// </summary>
    public string Title => IsEditMode ? _loc["EditGroup_EditTitle"] : _loc["EditGroup_AddTitle"];

    /// <summary>
    /// 是否可以變更名稱（編輯預設群組時不可變更）
    /// </summary>
    public bool CanChangeName => !IsEditMode || !IsDefaultGroup(_existingGroup?.Name);

    // === 群組屬性 ===
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _messageTemplate = "{message}";

    /// <summary>
    /// 可選擇的 Actions（帶勾選狀態）
    /// </summary>
    public ObservableCollection<SelectableActionItem> AvailableActions { get; } = new();

    // === 驗證 ===
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    /// <summary>
    /// 確認結果
    /// </summary>
    public bool DialogResult { get; private set; }

    /// <summary>
    /// 關閉請求事件
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// 建構子 - 新增模式
    /// </summary>
    public EditGroupViewModel() : this(null) { }

    /// <summary>
    /// 建構子 - 編輯模式
    /// </summary>
    public EditGroupViewModel(AlertGroup? existingGroup)
    {
        _existingGroup = existingGroup;
        _settings = ServiceLocator.SettingsService?.Load();

        // 載入可用的 Actions
        LoadAvailableActions();

        if (existingGroup != null)
        {
            // 編輯模式：載入現有設定
            LoadFromGroup(existingGroup);
        }
        else
        {
            // 新增模式：使用預設值
            MessageTemplate = "[Alert] {message}\n時間: {timestamp}";
        }
    }

    /// <summary>
    /// 載入可用的 Actions
    /// </summary>
    private void LoadAvailableActions()
    {
        if (_settings == null) return;

        foreach (var config in _settings.AlertActions)
        {
            var isSelected = _existingGroup?.ActionInstanceIds.Contains(config.InstanceId) ?? false;
            AvailableActions.Add(new SelectableActionItem(config, isSelected));
        }
    }

    /// <summary>
    /// 從現有群組載入
    /// </summary>
    private void LoadFromGroup(AlertGroup group)
    {
        Name = group.Name;
        Description = group.Description ?? string.Empty;
        IsEnabled = group.IsEnabled;
        MessageTemplate = group.MessageTemplate;
    }

    /// <summary>
    /// 檢查是否為預設群組
    /// </summary>
    private static bool IsDefaultGroup(string? name)
    {
        return name is "Default" or "Critical" or "Warning" or "Info";
    }

    /// <summary>
    /// 驗證輸入
    /// </summary>
    private bool Validate()
    {
        ErrorMessage = string.Empty;
        HasError = false;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "群組名稱不可為空";
            HasError = true;
            return false;
        }

        // 嚴格的命名規則驗證
        if (!AlertGroup.IsValidName(Name))
        {
            ErrorMessage = "群組名稱只能包含英文字母、數字、底線(_)、連字號(-)，且必須以英文字母開頭";
            HasError = true;
            return false;
        }

        if (string.IsNullOrWhiteSpace(MessageTemplate))
        {
            ErrorMessage = "訊息範本不可為空";
            HasError = true;
            return false;
        }

        // 檢查名稱是否與其他群組重複（新增或更名時）
        if (_settings != null && !IsEditMode)
        {
            if (_settings.AlertGroups.Any(g => g.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorMessage = "群組名稱已存在";
                HasError = true;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 建立 AlertGroup
    /// </summary>
    public AlertGroup? CreateGroup()
    {
        if (!Validate()) return null;

        var selectedActionIds = AvailableActions
            .Where(a => a.IsSelected)
            .Select(a => a.InstanceId)
            .ToList();

        return new AlertGroup
        {
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsEnabled = IsEnabled,
            MessageTemplate = MessageTemplate,
            ActionInstanceIds = selectedActionIds
        };
    }

    /// <summary>
    /// 全選 Actions
    /// </summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var action in AvailableActions)
        {
            action.IsSelected = true;
        }
    }

    /// <summary>
    /// 取消全選
    /// </summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var action in AvailableActions)
        {
            action.IsSelected = false;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!Validate()) return;

        DialogResult = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke();
    }
}

/// <summary>
/// 可選擇的 Action 項目（用於 CheckBox 清單）
/// </summary>
public partial class SelectableActionItem : ObservableObject
{
    public string InstanceId { get; }
    public string ActionTypeName { get; }
    public string DisplayText { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SelectableActionItem(AlertActionConfig config, bool isSelected)
    {
        InstanceId = config.InstanceId;
        ActionTypeName = AlertActionFactory.GetDisplayName(config.ActionType);
        DisplayText = $"{config.InstanceId} ({ActionTypeName})";
        IsSelected = isSelected;
    }
}
