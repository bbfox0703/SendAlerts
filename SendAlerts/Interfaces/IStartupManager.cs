namespace SendAlerts.Interfaces;

/// <summary>
/// 開機自動啟動管理介面
/// </summary>
public interface IStartupManager
{
    bool IsSupported { get; }
    bool IsStartupEnabled();
    bool EnableStartup();
    bool DisableStartup();
    bool ToggleStartup();
}
