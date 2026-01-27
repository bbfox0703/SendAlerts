using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _16pin_vmon.Models;

/// <summary>
/// TA1-4: Named Pipe 訊息資料模型
/// 外部工具 (如 HWiNFO64) 傳送的 JSON 格式訊息
/// </summary>
public class PipeMessage
{
    /// <summary>
    /// 目標警報群組名稱
    /// 對應 AlertGroup.Name
    /// </summary>
    [JsonPropertyName("GroupName")]
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// 自訂訊息內容
    /// 若提供則覆蓋群組的預設訊息範本
    /// </summary>
    [JsonPropertyName("CustomMessage")]
    public string? CustomMessage { get; set; }

    /// <summary>
    /// 訊息接收時間 (由系統自動填入)
    /// </summary>
    [JsonIgnore]
    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 原始 JSON 字串 (用於診斷)
    /// </summary>
    [JsonIgnore]
    public string? RawJson { get; set; }

    /// <summary>
    /// 是否有自訂訊息
    /// </summary>
    [JsonIgnore]
    public bool HasCustomMessage => !string.IsNullOrWhiteSpace(CustomMessage);

    /// <summary>
    /// 取得最終要發送的訊息
    /// </summary>
    /// <param name="groupTemplate">群組的預設訊息範本</param>
    /// <returns>CustomMessage 優先，否則使用群組範本</returns>
    public string GetEffectiveMessage(string? groupTemplate = null)
    {
        if (HasCustomMessage)
            return CustomMessage!;

        return groupTemplate ?? $"Alert triggered for group: {GroupName}";
    }
}

/// <summary>
/// PipeMessage 解析結果
/// </summary>
public class PipeMessageParseResult
{
    /// <summary>
    /// 是否解析成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 解析後的訊息物件 (成功時有值)
    /// </summary>
    public PipeMessage? Message { get; init; }

    /// <summary>
    /// 錯誤訊息 (失敗時有值)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 錯誤類型
    /// </summary>
    public PipeMessageError ErrorType { get; init; }

    /// <summary>
    /// 建立成功結果
    /// </summary>
    public static PipeMessageParseResult Ok(PipeMessage message) => new()
    {
        Success = true,
        Message = message,
        ErrorType = PipeMessageError.None
    };

    /// <summary>
    /// 建立失敗結果
    /// </summary>
    public static PipeMessageParseResult Fail(PipeMessageError errorType, string errorMessage) => new()
    {
        Success = false,
        ErrorType = errorType,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Pipe 訊息解析錯誤類型
/// </summary>
public enum PipeMessageError
{
    /// <summary>無錯誤</summary>
    None,

    /// <summary>訊息為空</summary>
    EmptyMessage,

    /// <summary>JSON 格式無效</summary>
    InvalidJson,

    /// <summary>缺少必要欄位 (GroupName)</summary>
    MissingRequiredField,

    /// <summary>GroupName 為空</summary>
    EmptyGroupName
}

/// <summary>
/// PipeMessage 解析器
/// </summary>
public static class PipeMessageParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 從 JSON 字串解析 PipeMessage
    /// </summary>
    /// <param name="json">JSON 字串</param>
    /// <returns>解析結果</returns>
    public static PipeMessageParseResult Parse(string? json)
    {
        // 檢查空訊息
        if (string.IsNullOrWhiteSpace(json))
        {
            return PipeMessageParseResult.Fail(
                PipeMessageError.EmptyMessage,
                "訊息內容為空");
        }

        try
        {
            // 嘗試解析 JSON
            var message = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

            if (message == null)
            {
                return PipeMessageParseResult.Fail(
                    PipeMessageError.InvalidJson,
                    "JSON 解析結果為 null");
            }

            // 驗證必要欄位
            if (string.IsNullOrWhiteSpace(message.GroupName))
            {
                return PipeMessageParseResult.Fail(
                    PipeMessageError.EmptyGroupName,
                    "GroupName 欄位為空或未提供");
            }

            // 設定原始 JSON
            message.RawJson = json;
            message.ReceivedAt = DateTime.Now;

            return PipeMessageParseResult.Ok(message);
        }
        catch (JsonException ex)
        {
            return PipeMessageParseResult.Fail(
                PipeMessageError.InvalidJson,
                $"JSON 解析失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// 將 PipeMessage 序列化為 JSON
    /// </summary>
    public static string ToJson(PipeMessage message)
    {
        return JsonSerializer.Serialize(message, JsonOptions);
    }
}
