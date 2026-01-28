using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;
using Window = Avalonia.Controls.Window;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// TD2-2/TD2-3: Windows 系統匣圖示管理器
/// 提供最小化到系統匣和右鍵選單功能
/// </summary>
public class TrayIconManager : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private bool _disposed;

    /// <summary>
    /// 是否支援系統匣功能 (僅 Windows)
    /// </summary>
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// 系統匣圖示是否可見
    /// </summary>
    public bool IsVisible => _notifyIcon?.Visible ?? false;

    /// <summary>
    /// 初始化系統匣圖示
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        if (!IsSupported)
        {
            Log.Debug("[TrayIcon] 系統匣功能僅支援 Windows");
            return;
        }

        _mainWindow = mainWindow;

        try
        {
            // 建立 NotifyIcon
            _notifyIcon = new NotifyIcon
            {
                Text = "SendAlerts - Hardware Monitor",
                Visible = false
            };

            // 設定圖示 (使用內嵌資源或系統圖示)
            _notifyIcon.Icon = LoadIcon();

            // TD2-3: 建立右鍵選單
            CreateContextMenu();

            // 雙擊顯示視窗
            _notifyIcon.DoubleClick += OnTrayIconDoubleClick;

            Log.Information("[TrayIcon] 系統匣圖示已初始化");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TrayIcon] 初始化失敗");
        }
    }

    /// <summary>
    /// TD2-3: 建立右鍵選單
    /// </summary>
    private void CreateContextMenu()
    {
        if (_notifyIcon == null) return;

        var contextMenu = new ContextMenuStrip();

        // 顯示/隱藏
        var showHideItem = new ToolStripMenuItem("Show Window");
        showHideItem.Click += (_, _) => ShowMainWindow();
        contextMenu.Items.Add(showHideItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Alert Actions
        var alertActionsItem = new ToolStripMenuItem("Alert Actions...");
        alertActionsItem.Click += (_, _) => OpenAlertActions();
        contextMenu.Items.Add(alertActionsItem);

        // Alert Groups
        var alertGroupsItem = new ToolStripMenuItem("Alert Groups...");
        alertGroupsItem.Click += (_, _) => OpenAlertGroups();
        contextMenu.Items.Add(alertGroupsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // TD2-1: 開機啟動選項
        var startupItem = new ToolStripMenuItem("Start with Windows");
        startupItem.Checked = StartupManager.IsStartupEnabled();
        startupItem.Click += (_, _) =>
        {
            StartupManager.ToggleStartup();
            startupItem.Checked = StartupManager.IsStartupEnabled();
        };
        contextMenu.Items.Add(startupItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // 退出
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// 載入圖示
    /// </summary>
    private Icon LoadIcon()
    {
        // 嘗試載入應用程式圖示
        try
        {
            var iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "icon.ico");

            if (System.IO.File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch
        {
            // 忽略錯誤
        }

        // Fallback: 使用系統預設圖示
        return SystemIcons.Application;
    }

    /// <summary>
    /// TD2-2: 最小化到系統匣
    /// </summary>
    public void MinimizeToTray()
    {
        if (_notifyIcon == null || _mainWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow.Hide();
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(
                2000,
                "SendAlerts",
                "Application minimized to system tray",
                ToolTipIcon.Info);
        });

        Log.Debug("[TrayIcon] 已最小化至系統匣");
    }

    /// <summary>
    /// 顯示主視窗
    /// </summary>
    public void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
        });

        Log.Debug("[TrayIcon] 已顯示主視窗");
    }

    /// <summary>
    /// 隱藏系統匣圖示
    /// </summary>
    public void Hide()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
    }

    /// <summary>
    /// 顯示系統匣圖示
    /// </summary>
    public void Show()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OpenAlertActions()
    {
        // 透過主視窗開啟 Alert Actions
        ShowMainWindow();
        // Note: 實際開啟對話框的邏輯可以透過事件或 ServiceLocator 實現
    }

    private void OpenAlertGroups()
    {
        // 透過主視窗開啟 Alert Groups
        ShowMainWindow();
        // Note: 實際開啟對話框的邏輯可以透過事件或 ServiceLocator 實現
    }

    private void ExitApplication()
    {
        Log.Information("[TrayIcon] 使用者從系統匣選擇退出");

        Dispatcher.UIThread.Post(() =>
        {
            _notifyIcon?.Dispose();
            _mainWindow?.Close();
            Environment.Exit(0);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }
}
