using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Models;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.ViewModels;

/// <summary>
/// TB2-1: 警報群組管理 ViewModel
/// </summary>
public partial class AlertGroupsViewModel : ViewModelBase
{
    private readonly ISettingsService? _settingsService;
    private AppSettings? _settings;

    /// <summary>
    /// 所有已設定的警報群組
    /// </summary>
    public ObservableCollection<AlertGroupItem> Groups { get; } = new();

    /// <summary>
    /// 目前選中的群組
    /// </summary>
    [ObservableProperty]
    private AlertGroupItem? _selectedGroup;

    /// <summary>
    /// 是否有選中的群組
    /// </summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>
    /// 狀態訊息
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 是否正在測試
    /// </summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>
    /// 視窗關閉請求事件
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// 開啟新增對話框請求事件
    /// </summary>
    public event Func<Task<AlertGroup?>>? AddGroupRequested;

    /// <summary>
    /// 開啟編輯對話框請求事件
    /// </summary>
    public event Func<AlertGroup, Task<AlertGroup?>>? EditGroupRequested;

    /// <summary>
    /// 設定已變更
    /// </summary>
    public bool HasChanges { get; private set; }

    public AlertGroupsViewModel()
    {
        _settingsService = ServiceLocator.SettingsService;
        LoadGroups();
    }

    /// <summary>
    /// 載入所有群組
    /// </summary>
    private void LoadGroups()
    {
        Groups.Clear();

        _settings = _settingsService?.Load();
        if (_settings == null) return;

        foreach (var group in _settings.AlertGroups)
        {
            Groups.Add(new AlertGroupItem(group, _settings.AlertActions.Count));
        }

        StatusMessage = $"已載入 {Groups.Count} 個群組";
        Log.Debug("[AlertGroupsViewModel] 載入 {Count} 個群組", Groups.Count);
    }

    partial void OnSelectedGroupChanged(AlertGroupItem? value)
    {
        HasSelection = value != null;
    }

    /// <summary>
    /// TB2-2: 新增群組
    /// </summary>
    [RelayCommand]
    private async Task AddGroupAsync()
    {
        if (_settings == null) return;

        var newGroup = AddGroupRequested != null ? await AddGroupRequested.Invoke() : null;

        if (newGroup != null)
        {
            // 檢查名稱是否重複
            if (_settings.AlertGroups.Any(g => g.Name.Equals(newGroup.Name, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"群組名稱已存在: {newGroup.Name}";
                return;
            }

            _settings.AlertGroups.Add(newGroup);
            Groups.Add(new AlertGroupItem(newGroup, _settings.AlertActions.Count));
            HasChanges = true;
            StatusMessage = $"已新增群組: {newGroup.Name}";
            Log.Information("[AlertGroupsViewModel] 新增群組: {Name}", newGroup.Name);
        }
    }

    /// <summary>
    /// TB2-2: 編輯選中的群組
    /// </summary>
    [RelayCommand]
    private async Task EditGroupAsync()
    {
        if (SelectedGroup == null || _settings == null) return;

        var existingGroup = _settings.AlertGroups.FirstOrDefault(g => g.Name == SelectedGroup.Name);
        if (existingGroup == null) return;

        var updatedGroup = EditGroupRequested != null ? await EditGroupRequested.Invoke(existingGroup) : null;

        if (updatedGroup != null)
        {
            // 更新設定
            var index = _settings.AlertGroups.IndexOf(existingGroup);
            if (index >= 0)
            {
                _settings.AlertGroups[index] = updatedGroup;
            }

            // 更新 UI
            var uiIndex = Groups.IndexOf(SelectedGroup);
            if (uiIndex >= 0)
            {
                Groups[uiIndex] = new AlertGroupItem(updatedGroup, _settings.AlertActions.Count);
                SelectedGroup = Groups[uiIndex];
            }

            HasChanges = true;
            StatusMessage = $"已更新群組: {updatedGroup.Name}";
            Log.Information("[AlertGroupsViewModel] 更新群組: {Name}", updatedGroup.Name);
        }
    }

    /// <summary>
    /// TB2-4: 刪除選中的群組
    /// </summary>
    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup == null || _settings == null) return;

        var groupName = SelectedGroup.Name;

        // 不允許刪除預設群組
        if (groupName is "Default" or "Critical" or "Warning" or "Info")
        {
            StatusMessage = $"無法刪除預設群組: {groupName}";
            return;
        }

        var groupToRemove = _settings.AlertGroups.FirstOrDefault(g => g.Name == groupName);
        if (groupToRemove != null)
        {
            _settings.AlertGroups.Remove(groupToRemove);
            Groups.Remove(SelectedGroup);
            HasChanges = true;
            StatusMessage = $"已刪除群組: {groupName}";
            Log.Information("[AlertGroupsViewModel] 刪除群組: {Name}", groupName);
        }
    }

    /// <summary>
    /// TB2-5: 測試選中的群組
    /// </summary>
    [RelayCommand]
    private async Task TestGroupAsync()
    {
        if (SelectedGroup == null) return;

        var alertService = ServiceLocator.AlertService;
        if (alertService == null)
        {
            StatusMessage = "AlertService 未初始化";
            return;
        }

        IsTesting = true;
        StatusMessage = $"測試中: {SelectedGroup.Name}...";

        try
        {
            var result = await alertService.TestGroupAsync(SelectedGroup.Name);
            StatusMessage = result.Success
                ? $"測試成功: {SelectedGroup.Name} ({result.ExecutedActions.Count} actions)"
                : $"測試失敗: {SelectedGroup.Name} - {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"測試錯誤: {ex.Message}";
            Log.Error(ex, "[AlertGroupsViewModel] 測試群組失敗: {Name}", SelectedGroup.Name);
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>
    /// 切換啟用狀態
    /// </summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        if (SelectedGroup == null || _settings == null) return;

        var group = _settings.AlertGroups.FirstOrDefault(g => g.Name == SelectedGroup.Name);
        if (group != null)
        {
            group.IsEnabled = !group.IsEnabled;
            SelectedGroup.IsEnabled = group.IsEnabled;
            HasChanges = true;
            StatusMessage = $"{SelectedGroup.Name}: {(group.IsEnabled ? "已啟用" : "已停用")}";
        }
    }

    /// <summary>
    /// 儲存變更
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (_settings == null || _settingsService == null) return;

        try
        {
            _settingsService.Save(_settings);

            // 重新載入 AlertService
            var alertService = ServiceLocator.AlertService;
            if (alertService != null)
            {
                alertService.LoadFromSettings(_settings.AlertActions, _settings.AlertGroups);
            }

            HasChanges = false;
            StatusMessage = "設定已儲存";
            Log.Information("[AlertGroupsViewModel] 設定已儲存");
        }
        catch (Exception ex)
        {
            StatusMessage = $"儲存失敗: {ex.Message}";
            Log.Error(ex, "[AlertGroupsViewModel] 儲存設定失敗");
        }
    }

    /// <summary>
    /// 關閉視窗
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// 重新載入
    /// </summary>
    [RelayCommand]
    private void Reload()
    {
        LoadGroups();
        HasChanges = false;
    }
}

/// <summary>
/// 用於 UI 綁定的 AlertGroup 包裝類別
/// </summary>
public partial class AlertGroupItem : ObservableObject
{
    private readonly AlertGroup _group;

    public string Name => _group.Name;
    public string? Description => _group.Description;
    public string MessageTemplate => _group.MessageTemplate;
    public int ActionCount => _group.ActionInstanceIds.Count;

    [ObservableProperty]
    private bool _isEnabled;

    public string ActionsSummary => ActionCount == 0
        ? "No actions"
        : $"{ActionCount} action{(ActionCount > 1 ? "s" : "")}";

    public AlertGroupItem(AlertGroup group, int totalActions)
    {
        _group = group;
        _isEnabled = group.IsEnabled;
    }
}
