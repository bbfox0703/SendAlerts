using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// TA3-2: 警報服務 - Alert Center 核心
/// 管理 AlertAction 實例和 AlertGroup 的 CRUD，並負責執行警報
/// </summary>
public class AlertService
{
    private readonly Dictionary<string, IAlertAction> _actions;
    private readonly Dictionary<string, AlertGroup> _groups;
    private readonly object _lock = new();

    /// <summary>
    /// 警報執行完成事件
    /// </summary>
    public event EventHandler<AlertExecutedEventArgs>? AlertExecuted;

    /// <summary>
    /// 警報執行失敗事件
    /// </summary>
    public event EventHandler<AlertErrorEventArgs>? AlertError;

    public AlertService()
    {
        _actions = new Dictionary<string, IAlertAction>(StringComparer.OrdinalIgnoreCase);
        _groups = new Dictionary<string, AlertGroup>(StringComparer.OrdinalIgnoreCase);
    }

    #region Action 管理

    /// <summary>
    /// 註冊警報動作實例
    /// </summary>
    public void RegisterAction(IAlertAction action)
    {
        if (action == null)
        {
            Log.Warning("[AlertService] 嘗試註冊 null Action");
            return;
        }

        lock (_lock)
        {
            if (_actions.ContainsKey(action.InstanceId))
            {
                Log.Warning("[AlertService] Action 已存在，將覆蓋: {InstanceId}", action.InstanceId);
            }

            _actions[action.InstanceId] = action;
            Log.Information("[AlertService] 註冊 Action: {InstanceId} ({Type})",
                action.InstanceId, action.ActionType);
        }
    }

    /// <summary>
    /// 從設定檔註冊警報動作
    /// </summary>
    public void RegisterAction(AlertActionConfig config)
    {
        var action = AlertActionFactory.Create(config);
        if (action != null)
        {
            RegisterAction(action);
        }
    }

    /// <summary>
    /// 批次註冊警報動作
    /// </summary>
    public void RegisterActions(IEnumerable<AlertActionConfig> configs)
    {
        foreach (var config in configs)
        {
            RegisterAction(config);
        }
    }

    /// <summary>
    /// 取消註冊警報動作
    /// </summary>
    public bool UnregisterAction(string instanceId)
    {
        lock (_lock)
        {
            if (_actions.Remove(instanceId))
            {
                Log.Information("[AlertService] 取消註冊 Action: {InstanceId}", instanceId);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 取得警報動作實例
    /// </summary>
    public IAlertAction? GetAction(string instanceId)
    {
        lock (_lock)
        {
            return _actions.TryGetValue(instanceId, out var action) ? action : null;
        }
    }

    /// <summary>
    /// 取得所有已註冊的警報動作
    /// </summary>
    public IReadOnlyList<IAlertAction> GetAllActions()
    {
        lock (_lock)
        {
            return _actions.Values.ToList();
        }
    }

    /// <summary>
    /// 取得已註冊的 Action 數量
    /// </summary>
    public int ActionCount
    {
        get { lock (_lock) { return _actions.Count; } }
    }

    #endregion

    #region Group 管理

    /// <summary>
    /// 註冊警報群組
    /// </summary>
    public void RegisterGroup(AlertGroup group)
    {
        if (group == null)
        {
            Log.Warning("[AlertService] 嘗試註冊 null Group");
            return;
        }

        var validation = group.Validate();
        if (!validation.IsValid)
        {
            Log.Warning("[AlertService] Group 驗證失敗: {Error}", validation.ErrorMessage);
            return;
        }

        lock (_lock)
        {
            if (_groups.ContainsKey(group.Name))
            {
                Log.Warning("[AlertService] Group 已存在，將覆蓋: {Name}", group.Name);
            }

            _groups[group.Name] = group;
            Log.Information("[AlertService] 註冊 Group: {Name} ({ActionCount} actions)",
                group.Name, group.ActionCount);
        }
    }

    /// <summary>
    /// 批次註冊警報群組
    /// </summary>
    public void RegisterGroups(IEnumerable<AlertGroup> groups)
    {
        foreach (var group in groups)
        {
            RegisterGroup(group);
        }
    }

    /// <summary>
    /// 取消註冊警報群組
    /// </summary>
    public bool UnregisterGroup(string groupName)
    {
        lock (_lock)
        {
            if (_groups.Remove(groupName))
            {
                Log.Information("[AlertService] 取消註冊 Group: {Name}", groupName);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 取得警報群組
    /// </summary>
    public AlertGroup? GetGroup(string groupName)
    {
        lock (_lock)
        {
            return _groups.TryGetValue(groupName, out var group) ? group : null;
        }
    }

    /// <summary>
    /// 取得所有已註冊的警報群組
    /// </summary>
    public IReadOnlyList<AlertGroup> GetAllGroups()
    {
        lock (_lock)
        {
            return _groups.Values.ToList();
        }
    }

    /// <summary>
    /// 取得已註冊的 Group 數量
    /// </summary>
    public int GroupCount
    {
        get { lock (_lock) { return _groups.Count; } }
    }

    /// <summary>
    /// 檢查群組是否存在
    /// </summary>
    public bool GroupExists(string groupName)
    {
        lock (_lock)
        {
            return _groups.ContainsKey(groupName);
        }
    }

    #endregion

    #region 警報執行

    /// <summary>
    /// 執行指定群組的所有警報動作
    /// </summary>
    /// <param name="groupName">群組名稱</param>
    /// <param name="customMessage">自訂訊息</param>
    /// <returns>執行結果</returns>
    public async Task<AlertExecutionResult> ExecuteGroupAsync(string groupName, string? customMessage = null)
    {
        var result = new AlertExecutionResult { GroupName = groupName };

        // 1. 查找群組
        AlertGroup? group;
        lock (_lock)
        {
            _groups.TryGetValue(groupName, out group);
        }

        if (group == null)
        {
            result.Success = false;
            result.ErrorMessage = $"找不到群組: {groupName}";
            Log.Warning("[AlertService] {Error}", result.ErrorMessage);
            AlertError?.Invoke(this, new AlertErrorEventArgs(groupName, result.ErrorMessage));
            return result;
        }

        // 2. 檢查群組是否啟用
        if (!group.IsEnabled)
        {
            result.Success = false;
            result.ErrorMessage = $"群組已停用: {groupName}";
            Log.Debug("[AlertService] {Error}", result.ErrorMessage);
            return result;
        }

        // 3. 格式化訊息
        var formattedMessage = group.GetEffectiveMessage(customMessage);
        result.FormattedMessage = formattedMessage;

        Log.Information("[AlertService] 執行群組警報: {GroupName} | 訊息: {Message}",
            groupName, formattedMessage);

        // 4. 取得並執行所有 Actions
        var actionsToExecute = new List<IAlertAction>();
        lock (_lock)
        {
            foreach (var actionId in group.ActionInstanceIds)
            {
                if (_actions.TryGetValue(actionId, out var action))
                {
                    actionsToExecute.Add(action);
                }
                else
                {
                    Log.Warning("[AlertService] 找不到 Action: {ActionId} (Group: {GroupName})",
                        actionId, groupName);
                    result.MissingActions.Add(actionId);
                }
            }
        }

        // 5. 並行執行所有 Actions
        var executionTasks = actionsToExecute
            .Where(a => a.IsEnabled)
            .Select(async action =>
            {
                try
                {
                    await action.ExecuteAsync(formattedMessage);
                    result.ExecutedActions.Add(action.InstanceId);
                    Log.Debug("[AlertService] Action 執行成功: {InstanceId}", action.InstanceId);
                }
                catch (Exception ex)
                {
                    result.FailedActions.Add(action.InstanceId);
                    Log.Error(ex, "[AlertService] Action 執行失敗: {InstanceId}", action.InstanceId);
                }
            });

        await Task.WhenAll(executionTasks);

        // 6. 設定結果
        result.Success = result.FailedActions.Count == 0 && result.MissingActions.Count == 0;
        result.TotalActions = group.ActionInstanceIds.Count;

        Log.Information("[AlertService] 群組執行完成: {GroupName} | 成功: {Executed}/{Total}",
            groupName, result.ExecutedActions.Count, result.TotalActions);

        // 7. 觸發事件
        AlertExecuted?.Invoke(this, new AlertExecutedEventArgs(result));

        // TB3-3: 記錄到歷史
        ServiceLocator.AddAlertHistory(groupName, formattedMessage, result.Success);

        return result;
    }

    /// <summary>
    /// 從 Named Pipe 訊息執行警報
    /// </summary>
    public async Task<AlertExecutionResult> ExecuteAsync(PipeMessage message)
    {
        if (message == null)
        {
            return new AlertExecutionResult
            {
                Success = false,
                ErrorMessage = "PipeMessage 為 null"
            };
        }

        return await ExecuteGroupAsync(message.GroupName, message.CustomMessage);
    }

    /// <summary>
    /// 測試執行單一 Action (不經過群組)
    /// </summary>
    public async Task<bool> TestActionAsync(string instanceId, string testMessage = "測試訊息")
    {
        var action = GetAction(instanceId);
        if (action == null)
        {
            Log.Warning("[AlertService] 測試失敗 - 找不到 Action: {InstanceId}", instanceId);
            return false;
        }

        try
        {
            Log.Information("[AlertService] 測試 Action: {InstanceId}", instanceId);
            await action.ExecuteAsync($"[TEST] {testMessage}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AlertService] 測試 Action 失敗: {InstanceId}", instanceId);
            return false;
        }
    }

    /// <summary>
    /// 測試執行整個群組
    /// </summary>
    public async Task<AlertExecutionResult> TestGroupAsync(string groupName)
    {
        return await ExecuteGroupAsync(groupName, "[TEST] 群組測試訊息");
    }

    #endregion

    #region 初始化輔助

    /// <summary>
    /// 清除所有已註冊的 Actions 和 Groups
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _actions.Clear();
            _groups.Clear();
            Log.Information("[AlertService] 已清除所有 Actions 和 Groups");
        }
    }

    /// <summary>
    /// 從設定載入 Actions 和 Groups
    /// </summary>
    public void LoadFromSettings(IEnumerable<AlertActionConfig> actionConfigs, IEnumerable<AlertGroup> groups)
    {
        Clear();
        RegisterActions(actionConfigs);
        RegisterGroups(groups);

        Log.Information("[AlertService] 設定載入完成: {ActionCount} Actions, {GroupCount} Groups",
            ActionCount, GroupCount);
    }

    /// <summary>
    /// 建立預設配置 (用於首次啟動)
    /// </summary>
    public void InitializeDefaults()
    {
        // 建立預設群組
        RegisterGroup(AlertGroup.CreateDefault());
        RegisterGroup(AlertGroup.CreateCritical());
        RegisterGroup(AlertGroup.CreateWarning());
        RegisterGroup(AlertGroup.CreateInfo());

        Log.Information("[AlertService] 預設配置已初始化");
    }

    #endregion
}

#region 事件參數類別

/// <summary>
/// 警報執行結果
/// </summary>
public class AlertExecutionResult
{
    /// <summary>執行是否成功 (所有 Action 都成功)</summary>
    public bool Success { get; set; }

    /// <summary>群組名稱</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>格式化後的訊息</summary>
    public string FormattedMessage { get; set; } = string.Empty;

    /// <summary>錯誤訊息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>群組中的 Action 總數</summary>
    public int TotalActions { get; set; }

    /// <summary>成功執行的 Action ID 清單</summary>
    public List<string> ExecutedActions { get; } = new();

    /// <summary>執行失敗的 Action ID 清單</summary>
    public List<string> FailedActions { get; } = new();

    /// <summary>找不到的 Action ID 清單</summary>
    public List<string> MissingActions { get; } = new();
}

/// <summary>
/// 警報執行完成事件參數
/// </summary>
public class AlertExecutedEventArgs : EventArgs
{
    public AlertExecutionResult Result { get; }
    public DateTime ExecutedAt { get; }

    public AlertExecutedEventArgs(AlertExecutionResult result)
    {
        Result = result;
        ExecutedAt = DateTime.Now;
    }
}

/// <summary>
/// 警報執行錯誤事件參數
/// </summary>
public class AlertErrorEventArgs : EventArgs
{
    public string GroupName { get; }
    public string ErrorMessage { get; }
    public DateTime OccurredAt { get; }

    public AlertErrorEventArgs(string groupName, string errorMessage)
    {
        GroupName = groupName;
        ErrorMessage = errorMessage;
        OccurredAt = DateTime.Now;
    }
}

#endregion
