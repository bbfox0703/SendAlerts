// File: /16pin_vmon.Core/Interfaces/IAlertAction.cs
public interface IAlertAction
{
    string ActionName { get; }
    bool IsEnabled { get; set; }
    Task ExecuteAsync(string message);
    void ShowConfigurationUI(); // 讓不同 Action 彈出自己的設定視窗
}