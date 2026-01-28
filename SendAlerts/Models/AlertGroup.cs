using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SendAlerts.Core.Interfaces;

namespace SendAlerts.Models;

/// <summary>
/// TA3-1: 警報群組資料模型
/// 群組將多個 AlertAction 綁定在一起，並提供共用的訊息範本
/// </summary>
public class AlertGroup
{
    /// <summary>
    /// 群組名稱 (例如: "Critical", "Warning", "Info")
    /// 用於 Named Pipe 訊息中的 GroupName 對應
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 群組描述 (UI 顯示用)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否啟用此群組
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 訊息範本 (支援變數替換)
    /// 可用變數: {message}, {timestamp}, {group_name}, {date}, {time}
    /// </summary>
    public string MessageTemplate { get; set; } = "{message}";

    /// <summary>
    /// 動作實例 ID 清單 (引用 AlertActionConfig.InstanceId)
    /// </summary>
    public List<string> ActionInstanceIds { get; set; } = new();

    #region 訊息處理

    /// <summary>
    /// TA3-3: 將訊息範本中的變數替換為實際值
    /// </summary>
    /// <param name="customMessage">自訂訊息 (來自 Pipe 或其他來源)</param>
    /// <returns>替換變數後的完整訊息</returns>
    public string FormatMessage(string? customMessage)
    {
        var now = DateTime.Now;
        var message = customMessage ?? string.Empty;

        return MessageTemplate
            .Replace("{message}", message)
            .Replace("{timestamp}", now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{group_name}", Name)
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HH:mm:ss"));
    }

    /// <summary>
    /// TA3-4: 取得最終要發送的訊息
    /// 優先順序: CustomMessage (若非空) > 使用 MessageTemplate 格式化
    /// </summary>
    /// <param name="customMessage">來自 Pipe 的自訂訊息</param>
    /// <param name="useTemplateWhenCustomEmpty">當 customMessage 為空時是否使用範本</param>
    /// <returns>最終訊息</returns>
    public string GetEffectiveMessage(string? customMessage, bool useTemplateWhenCustomEmpty = true)
    {
        // 如果有自訂訊息且範本就是 {message}，直接返回自訂訊息
        if (!string.IsNullOrWhiteSpace(customMessage) && MessageTemplate == "{message}")
        {
            return customMessage;
        }

        // 如果有自訂訊息，使用範本格式化
        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            return FormatMessage(customMessage);
        }

        // 沒有自訂訊息時
        if (useTemplateWhenCustomEmpty)
        {
            // 使用範本但移除空的 {message} 佔位符
            return FormatMessage(string.Empty).Replace("{message}", "").Trim();
        }

        return string.Empty;
    }

    #endregion

    #region 驗證

    /// <summary>
    /// 群組名稱的驗證正規表達式
    /// 只允許: 英文字母(a-z, A-Z)、數字(0-9)、底線(_)、連字號(-)
    /// 必須以英文字母開頭
    /// </summary>
    private static readonly Regex ValidNamePattern = new(@"^[a-zA-Z][a-zA-Z0-9_-]*$", RegexOptions.Compiled);

    /// <summary>
    /// 驗證群組名稱格式是否有效
    /// </summary>
    /// <param name="name">群組名稱</param>
    /// <returns>是否有效</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return ValidNamePattern.IsMatch(name);
    }

    /// <summary>
    /// 驗證群組設定是否有效
    /// </summary>
    public AlertGroupValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return AlertGroupValidationResult.Invalid("群組名稱不可為空");

        if (!IsValidName(Name))
            return AlertGroupValidationResult.Invalid(
                "群組名稱只能包含英文字母、數字、底線(_)、連字號(-)，且必須以英文字母開頭");

        if (string.IsNullOrWhiteSpace(MessageTemplate))
            return AlertGroupValidationResult.Invalid("訊息範本不可為空");

        // ActionInstanceIds 可以為空（允許空群組）

        return AlertGroupValidationResult.Valid();
    }

    /// <summary>
    /// 驗證群組設定，並檢查引用的 Action 是否存在
    /// </summary>
    /// <param name="availableActionIds">可用的 Action InstanceId 清單</param>
    public AlertGroupValidationResult ValidateWithActions(IEnumerable<string> availableActionIds)
    {
        var basicValidation = Validate();
        if (!basicValidation.IsValid)
            return basicValidation;

        var availableSet = new HashSet<string>(availableActionIds, StringComparer.OrdinalIgnoreCase);
        var missingIds = ActionInstanceIds
            .Where(id => !availableSet.Contains(id))
            .ToList();

        if (missingIds.Count > 0)
        {
            return AlertGroupValidationResult.Invalid(
                $"找不到以下 Action: {string.Join(", ", missingIds)}");
        }

        return AlertGroupValidationResult.Valid();
    }

    #endregion

    #region 工廠方法

    /// <summary>
    /// 建立預設群組 (Default)
    /// </summary>
    public static AlertGroup CreateDefault()
    {
        return new AlertGroup
        {
            Name = "Default",
            Description = "預設警報群組",
            IsEnabled = true,
            MessageTemplate = "[Alert] {message}\n時間: {timestamp}",
            ActionInstanceIds = new List<string>()
        };
    }

    /// <summary>
    /// 建立緊急群組 (Critical)
    /// </summary>
    public static AlertGroup CreateCritical(params string[] actionIds)
    {
        return new AlertGroup
        {
            Name = "Critical",
            Description = "緊急警報 - 高優先級",
            IsEnabled = true,
            MessageTemplate = "🚨 [CRITICAL] {message}\n群組: {group_name}\n時間: {timestamp}",
            ActionInstanceIds = new List<string>(actionIds)
        };
    }

    /// <summary>
    /// 建立警告群組 (Warning)
    /// </summary>
    public static AlertGroup CreateWarning(params string[] actionIds)
    {
        return new AlertGroup
        {
            Name = "Warning",
            Description = "警告通知 - 中優先級",
            IsEnabled = true,
            MessageTemplate = "⚠️ [WARNING] {message}\n時間: {timestamp}",
            ActionInstanceIds = new List<string>(actionIds)
        };
    }

    /// <summary>
    /// 建立資訊群組 (Info)
    /// </summary>
    public static AlertGroup CreateInfo(params string[] actionIds)
    {
        return new AlertGroup
        {
            Name = "Info",
            Description = "一般資訊通知",
            IsEnabled = true,
            MessageTemplate = "ℹ️ {message}",
            ActionInstanceIds = new List<string>(actionIds)
        };
    }

    /// <summary>
    /// 建立自訂群組
    /// </summary>
    public static AlertGroup Create(string name, string messageTemplate, params string[] actionIds)
    {
        return new AlertGroup
        {
            Name = name,
            IsEnabled = true,
            MessageTemplate = messageTemplate,
            ActionInstanceIds = new List<string>(actionIds)
        };
    }

    #endregion

    #region 輔助方法

    /// <summary>
    /// 新增 Action 到群組
    /// </summary>
    public void AddAction(string actionInstanceId)
    {
        if (!ActionInstanceIds.Contains(actionInstanceId, StringComparer.OrdinalIgnoreCase))
        {
            ActionInstanceIds.Add(actionInstanceId);
        }
    }

    /// <summary>
    /// 從群組移除 Action
    /// </summary>
    public bool RemoveAction(string actionInstanceId)
    {
        return ActionInstanceIds.RemoveAll(
            id => string.Equals(id, actionInstanceId, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>
    /// 檢查群組是否包含指定的 Action
    /// </summary>
    public bool ContainsAction(string actionInstanceId)
    {
        return ActionInstanceIds.Contains(actionInstanceId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 取得群組中的 Action 數量
    /// </summary>
    public int ActionCount => ActionInstanceIds.Count;

    /// <summary>
    /// 群組是否為空（沒有任何 Action）
    /// </summary>
    public bool IsEmpty => ActionInstanceIds.Count == 0;

    #endregion

    public override string ToString()
    {
        return $"{Name} ({ActionCount} actions, {(IsEnabled ? "Enabled" : "Disabled")})";
    }
}

/// <summary>
/// 警報群組驗證結果
/// </summary>
public class AlertGroupValidationResult
{
    /// <summary>是否有效</summary>
    public bool IsValid { get; init; }

    /// <summary>錯誤訊息 (無效時)</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>建立有效結果</summary>
    public static AlertGroupValidationResult Valid() => new() { IsValid = true };

    /// <summary>建立無效結果</summary>
    public static AlertGroupValidationResult Invalid(string errorMessage) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage
    };
}
