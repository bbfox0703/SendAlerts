using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.ViewModels;

/// <summary>
/// TB1-1: 警報動作管理 ViewModel
/// </summary>
public partial class AlertActionsViewModel : ViewModelBase
{
    private readonly ISettingsService? _settingsService;
    private AppSettings? _settings;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    #region Localized Strings
    public string Loc_Title => _loc["AlertActions_Title"];
    public string Loc_InstanceId => _loc["AlertActions_InstanceId"];
    public string Loc_Type => _loc["AlertActions_Type"];
    public string Loc_Description => _loc["AlertActions_Description"];
    public string Loc_Status => _loc["AlertActions_Status"];
    public string Loc_Add => _loc["Add"];
    public string Loc_Edit => _loc["Edit"];
    public string Loc_Delete => _loc["Delete"];
    public string Loc_Test => _loc["Test"];
    public string Loc_Toggle => _loc["AlertGroups_Toggle"];
    public string Loc_Reload => _loc["Reload"];
    public string Loc_Save => _loc["Save"];
    public string Loc_Close => _loc["Close"];
    public string Loc_Enabled => _loc["Enabled"];
    public string Loc_Disabled => _loc["Disabled"];
    #endregion

    /// <summary>
    /// 所有已設定的警報動作
    /// </summary>
    public ObservableCollection<AlertActionConfigItem> Actions { get; } = new();

    /// <summary>
    /// 目前選中的動作
    /// </summary>
    [ObservableProperty]
    private AlertActionConfigItem? _selectedAction;

    /// <summary>
    /// 是否有選中的動作
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
    /// 設定已變更
    /// </summary>
    public bool HasChanges { get; private set; }

    public AlertActionsViewModel()
    {
        _settingsService = ServiceLocator.SettingsService;
        LoadActions();
    }

    /// <summary>
    /// 載入所有動作
    /// </summary>
    private void LoadActions()
    {
        Actions.Clear();

        _settings = _settingsService?.Load();
        if (_settings == null) return;

        foreach (var config in _settings.AlertActions)
        {
            Actions.Add(new AlertActionConfigItem(config));
        }

        StatusMessage = _loc.GetString("Msg_Loaded", Actions.Count);
        Log.Debug("[AlertActionsViewModel] 載入 {Count} 個動作", Actions.Count);
    }

    partial void OnSelectedActionChanged(AlertActionConfigItem? value)
    {
        HasSelection = value != null;
    }

    /// <summary>
    /// 開啟新增對話框請求事件
    /// </summary>
    public event Func<Task<AlertActionConfig?>>? AddActionRequested;

    /// <summary>
    /// 開啟編輯對話框請求事件
    /// </summary>
    public event Func<AlertActionConfig, Task<AlertActionConfig?>>? EditActionRequested;

    /// <summary>
    /// TB1-2: 新增動作
    /// </summary>
    [RelayCommand]
    private async Task AddActionAsync()
    {
        if (_settings == null) return;

        var newConfig = AddActionRequested != null ? await AddActionRequested.Invoke() : null;

        if (newConfig != null)
        {
            // 檢查 InstanceId 是否重複
            if (_settings.AlertActions.Any(a => a.InstanceId.Equals(newConfig.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = _loc.GetString("Msg_DuplicateId", newConfig.InstanceId);
                return;
            }

            _settings.AlertActions.Add(newConfig);
            Actions.Add(new AlertActionConfigItem(newConfig));
            HasChanges = true;
            StatusMessage = _loc.GetString("Msg_Added", newConfig.InstanceId);
            Log.Information("[AlertActionsViewModel] 新增動作: {InstanceId} ({Type})", newConfig.InstanceId, newConfig.ActionType);
        }
    }

    /// <summary>
    /// TB1-3: 編輯選中的動作
    /// </summary>
    [RelayCommand]
    private async Task EditActionAsync()
    {
        if (SelectedAction == null || _settings == null) return;

        var existingConfig = _settings.AlertActions.FirstOrDefault(a => a.InstanceId == SelectedAction.InstanceId);
        if (existingConfig == null) return;

        var updatedConfig = EditActionRequested != null ? await EditActionRequested.Invoke(existingConfig) : null;

        if (updatedConfig != null)
        {
            // 更新設定
            var index = _settings.AlertActions.IndexOf(existingConfig);
            if (index >= 0)
            {
                _settings.AlertActions[index] = updatedConfig;
            }

            // 更新 UI
            var uiIndex = Actions.IndexOf(SelectedAction);
            if (uiIndex >= 0)
            {
                Actions[uiIndex] = new AlertActionConfigItem(updatedConfig);
                SelectedAction = Actions[uiIndex];
            }

            HasChanges = true;
            StatusMessage = _loc.GetString("Msg_Updated", updatedConfig.InstanceId);
            Log.Information("[AlertActionsViewModel] 更新動作: {InstanceId}", updatedConfig.InstanceId);
        }
    }

    /// <summary>
    /// 刪除選中的動作
    /// </summary>
    [RelayCommand]
    private void DeleteAction()
    {
        if (SelectedAction == null || _settings == null) return;

        var instanceId = SelectedAction.InstanceId;

        // TB1-4: 檢查是否被 Group 引用
        var referencingGroups = _settings.AlertGroups
            .Where(g => g.ActionInstanceIds.Contains(instanceId))
            .Select(g => g.Name)
            .ToList();

        if (referencingGroups.Count > 0)
        {
            StatusMessage = _loc.GetString("Msg_CannotDeleteReferenced", string.Join(", ", referencingGroups));
            return;
        }

        // 移除動作
        var configToRemove = _settings.AlertActions.FirstOrDefault(a => a.InstanceId == instanceId);
        if (configToRemove != null)
        {
            _settings.AlertActions.Remove(configToRemove);
            Actions.Remove(SelectedAction);
            HasChanges = true;
            StatusMessage = _loc.GetString("Msg_Deleted", instanceId);
            Log.Information("[AlertActionsViewModel] 刪除動作: {InstanceId}", instanceId);
        }
    }

    /// <summary>
    /// 測試選中的動作
    /// </summary>
    [RelayCommand]
    private async Task TestActionAsync()
    {
        if (SelectedAction == null || _settings == null) return;

        // 從設定中找到對應的 config
        var config = _settings.AlertActions.FirstOrDefault(a => a.InstanceId == SelectedAction.InstanceId);
        if (config == null)
        {
            StatusMessage = _loc.GetString("Msg_ConfigNotFound", SelectedAction.InstanceId);
            return;
        }

        IsTesting = true;
        StatusMessage = _loc.GetString("Msg_Testing", SelectedAction.InstanceId);

        try
        {
            // 直接從 config 建立 action 來測試（不需要先儲存）
            var action = AlertActionFactory.Create(config);
            if (action == null)
            {
                StatusMessage = _loc.GetString("Msg_CannotCreateAction", SelectedAction.InstanceId);
                return;
            }

            Log.Information("[AlertActionsViewModel] 測試 Action: {InstanceId}", SelectedAction.InstanceId);
            var result = await action.ExecuteAsync($"{_loc["Msg_ActionTestMessage"]} - {DateTime.Now:HH:mm:ss}");
            StatusMessage = result.Success
                ? _loc.GetString("Msg_ActionSent", SelectedAction.InstanceId, result.Message)
                : _loc.GetString("Msg_ActionSendFailed", SelectedAction.InstanceId, result.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = _loc.GetString("Msg_TestError", ex.Message);
            Log.Error(ex, "[AlertActionsViewModel] Test action failed: {InstanceId}", SelectedAction.InstanceId);
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
        if (SelectedAction == null || _settings == null) return;

        var config = _settings.AlertActions.FirstOrDefault(a => a.InstanceId == SelectedAction.InstanceId);
        if (config != null)
        {
            config.IsEnabled = !config.IsEnabled;
            SelectedAction.IsEnabled = config.IsEnabled;
            HasChanges = true;
            StatusMessage = _loc.GetString("Msg_Toggled", SelectedAction.InstanceId, config.IsEnabled ? _loc["Enabled"] : _loc["Disabled"]);
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
            StatusMessage = _loc["Msg_SettingsSaved"];
            Log.Information("[AlertActionsViewModel] 設定已儲存");
        }
        catch (Exception ex)
        {
            StatusMessage = _loc.GetString("Msg_SaveFailed", ex.Message);
            Log.Error(ex, "[AlertActionsViewModel] 儲存設定失敗");
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
        LoadActions();
        HasChanges = false;
    }
}

/// <summary>
/// 用於 UI 綁定的 AlertActionConfig 包裝類別
/// </summary>
public partial class AlertActionConfigItem : ObservableObject
{
    private readonly AlertActionConfig _config;

    public string InstanceId => _config.InstanceId;
    public AlertActionType ActionType => _config.ActionType;
    public string ActionTypeName => AlertActionFactory.GetDisplayName(_config.ActionType);

    [ObservableProperty]
    private bool _isEnabled;

    public string Description => GetDescription();

    public AlertActionConfigItem(AlertActionConfig config)
    {
        _config = config;
        _isEnabled = config.IsEnabled;
    }

    private string GetDescription()
    {
        return _config.ActionType switch
        {
            AlertActionType.CommandLine => TruncateCommand(_config.Command ?? ""),
            AlertActionType.Telegram => $"Chat: {_config.TelegramChatId}",
            AlertActionType.Discord => TruncateUrl(_config.DiscordWebhookUrl ?? ""),
            AlertActionType.Email => _config.EmailToAddresses ?? "",
            AlertActionType.HttpWebhook => _config.WebhookUrl ?? "",
            AlertActionType.SystemShutdown => _config.ShutdownType ?? "Shutdown",
            _ => ""
        };
    }

    private static string TruncateCommand(string command)
    {
        const int maxLength = 40;
        if (command.Length <= maxLength) return command;
        return command[..(maxLength - 3)] + "...";
    }

    private static string TruncateUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        // 顯示簡化的 Discord Webhook 資訊
        if (url.Contains("discord.com/api/webhooks/"))
        {
            var parts = url.Split('/');
            if (parts.Length >= 2)
            {
                return $"Webhook: ...{parts[^2][^6..]}";
            }
        }
        const int maxLength = 30;
        if (url.Length <= maxLength) return url;
        return url[..(maxLength - 3)] + "...";
    }
}
