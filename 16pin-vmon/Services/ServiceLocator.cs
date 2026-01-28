using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon.Services;

/// <summary>
/// T1-1: 簡易服務定位器，用於跨專案依賴注入
/// 在應用程式啟動時由 Platform Head (Desktop) 設定具體實作
/// </summary>
public static class ServiceLocator
{
    /// <summary>
    /// GPU 資料提供者（由 Desktop 專案根據平台設定）
    /// </summary>
    public static IGpuProvider? GpuProvider { get; set; }

    /// <summary>
    /// 設定服務
    /// </summary>
    public static ISettingsService? SettingsService { get; set; }

    /// <summary>
    /// TA3-2: 警報服務 (Alert Center 核心)
    /// </summary>
    public static AlertService? AlertService { get; set; }

    #region TD2: 系統整合

    /// <summary>
    /// TD2-2: 最小化到系統匣的委派
    /// </summary>
    public static Action? MinimizeToTray { get; set; }

    /// <summary>
    /// TD2-2: 從系統匣還原視窗的委派
    /// </summary>
    public static Action? RestoreFromTray { get; set; }

    /// <summary>
    /// TD2-1: 是否以最小化模式啟動
    /// </summary>
    public static bool StartMinimized { get; set; }

    #endregion

    #region TB3-2: Named Pipe 狀態

    private static bool _isPipeServerRunning;

    /// <summary>
    /// Named Pipe Server 是否正在運行
    /// </summary>
    public static bool IsPipeServerRunning
    {
        get => _isPipeServerRunning;
        set
        {
            if (_isPipeServerRunning != value)
            {
                _isPipeServerRunning = value;
                PipeStatusChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Pipe 狀態變更事件
    /// </summary>
    public static event EventHandler? PipeStatusChanged;

    #endregion

    #region TB3-3: 最近警報歷史

    private static readonly List<AlertHistoryItem> _alertHistory = new();
    private static readonly object _historyLock = new();
    private const int MaxHistoryCount = 10;

    /// <summary>
    /// 取得最近的警報歷史紀錄（唯讀）
    /// </summary>
    public static IReadOnlyList<AlertHistoryItem> AlertHistory
    {
        get
        {
            lock (_historyLock)
            {
                return _alertHistory.ToArray();
            }
        }
    }

    /// <summary>
    /// 新增警報歷史紀錄
    /// </summary>
    public static void AddAlertHistory(string groupName, string message, bool success)
    {
        lock (_historyLock)
        {
            var item = new AlertHistoryItem(groupName, message, success);
            _alertHistory.Insert(0, item);

            // 保留最近 10 筆
            while (_alertHistory.Count > MaxHistoryCount)
            {
                _alertHistory.RemoveAt(_alertHistory.Count - 1);
            }
        }

        AlertHistoryChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 清除歷史紀錄
    /// </summary>
    public static void ClearAlertHistory()
    {
        lock (_historyLock)
        {
            _alertHistory.Clear();
        }
        AlertHistoryChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 警報歷史變更事件
    /// </summary>
    public static event EventHandler? AlertHistoryChanged;

    #endregion
}

/// <summary>
/// TB3-3: 警報歷史紀錄項目
/// </summary>
public class AlertHistoryItem
{
    /// <summary>觸發時間</summary>
    public DateTime Timestamp { get; }

    /// <summary>群組名稱</summary>
    public string GroupName { get; }

    /// <summary>訊息內容</summary>
    public string Message { get; }

    /// <summary>是否執行成功</summary>
    public bool Success { get; }

    /// <summary>格式化的時間字串</summary>
    public string TimeString => Timestamp.ToString("HH:mm:ss");

    /// <summary>顯示文字</summary>
    public string DisplayText => $"[{TimeString}] {GroupName}: {(Message.Length > 30 ? Message[..30] + "..." : Message)}";

    public AlertHistoryItem(string groupName, string message, bool success)
    {
        Timestamp = DateTime.Now;
        GroupName = groupName;
        Message = message;
        Success = success;
    }
}
