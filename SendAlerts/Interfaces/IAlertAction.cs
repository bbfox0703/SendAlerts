using System.Threading.Tasks;

namespace SendAlerts.Core.Interfaces;

/// <summary>
/// 警報動作類型
/// </summary>
public enum AlertActionType
{
    /// <summary>命令列執行</summary>
    CommandLine,

    /// <summary>Telegram Bot</summary>
    Telegram,

    /// <summary>Discord Webhook</summary>
    Discord,

    /// <summary>電子郵件 (SMTP)</summary>
    Email,

    /// <summary>HTTP Webhook</summary>
    HttpWebhook,

    /// <summary>系統關機</summary>
    SystemShutdown
}

/// <summary>
/// TA2-1: 警報動作介面 (支援多實例)
/// </summary>
public interface IAlertAction
{
    /// <summary>
    /// 實例唯一識別碼 (例如: "LINE_User1", "Telegram_Admin")
    /// 用於在群組中引用特定的動作實例
    /// </summary>
    string InstanceId { get; }

    /// <summary>
    /// 動作類型
    /// </summary>
    AlertActionType ActionType { get; }

    /// <summary>
    /// 顯示名稱 (用於 UI 顯示)
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 是否啟用
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 執行警報動作
    /// </summary>
    /// <param name="message">警報訊息</param>
    /// <returns>執行結果，包含 API 回應狀態</returns>
    Task<AlertActionExecuteResult> ExecuteAsync(string message);

    /// <summary>
    /// 驗證設定是否有效
    /// </summary>
    /// <returns>驗證結果</returns>
    AlertActionValidationResult Validate();
}

/// <summary>
/// 警報動作執行結果
/// </summary>
public class AlertActionExecuteResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? StatusCode { get; init; }

    public static AlertActionExecuteResult Ok(string message = "執行成功") =>
        new() { Success = true, Message = message };

    public static AlertActionExecuteResult Skipped(string reason) =>
        new() { Success = true, Message = reason };

    public static AlertActionExecuteResult Fail(string message, int? statusCode = null) =>
        new() { Success = false, Message = message, StatusCode = statusCode };
}

/// <summary>
/// 警報動作驗證結果
/// </summary>
public class AlertActionValidationResult
{
    /// <summary>是否有效</summary>
    public bool IsValid { get; init; }

    /// <summary>錯誤訊息 (無效時)</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>建立有效結果</summary>
    public static AlertActionValidationResult Valid() => new() { IsValid = true };

    /// <summary>建立無效結果</summary>
    public static AlertActionValidationResult Invalid(string errorMessage) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage
    };
}