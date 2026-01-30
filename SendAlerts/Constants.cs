namespace SendAlerts;

/// <summary>
/// 跨模組共用常數集中管理
/// </summary>
public static class AppConstants
{
    /// <summary>Named Pipe 名稱 (不含 \\.\pipe\ 前綴)</summary>
    public const string PipeName = "sendalerts-pipe";

    /// <summary>Alert Action 預設冷卻時間 (秒)</summary>
    public const int DefaultCooldownSeconds = 30;

    /// <summary>CommandLineAlertAction 命令執行逾時 (毫秒)</summary>
    public const int CommandExecuteTimeoutMs = 30_000;

    /// <summary>取樣間隔下限 (秒)</summary>
    public const int SamplingIntervalMin = 1;

    /// <summary>取樣間隔上限 (秒)</summary>
    public const int SamplingIntervalMax = 10;
}
