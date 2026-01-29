using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// TD2-2/TD2-3: 系統匣圖示管理器 (Avalonia Native TrayIcon)
/// 提供最小化到系統匣和右鍵選單功能
/// </summary>
public class TrayIconManager : IDisposable
{
    private TrayIcon? _trayIcon;
    private Window? _mainWindow;
    private bool _disposed;

    /// <summary>
    /// 是否支援系統匣功能 (Windows / Linux with tray support)
    /// </summary>
    public static bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// 系統匣圖示是否可見
    /// </summary>
    public bool IsVisible => _trayIcon?.IsVisible ?? false;

    /// <summary>
    /// 初始化系統匣圖示
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        if (!IsSupported)
        {
            Log.Debug("[TrayIcon] 系統匣功能不支援此平台");
            return;
        }

        _mainWindow = mainWindow;

        try
        {
            _trayIcon = new TrayIcon
            {
                ToolTipText = "SendAlerts - Hardware Alert Relay Station",
                IsVisible = false,
                Menu = CreateContextMenu()
            };

            // 載入圖示
            LoadIcon();

            // 雙擊/點擊顯示視窗
            _trayIcon.Clicked += OnTrayIconClicked;

            Log.Information("[TrayIcon] 系統匣圖示已初始化 (Avalonia Native)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TrayIcon] 初始化失敗");
        }
    }

    /// <summary>
    /// TD2-3: 建立右鍵選單
    /// </summary>
    private NativeMenu CreateContextMenu()
    {
        var loc = LocalizationService.Instance;
        var menu = new NativeMenu();

        // 顯示視窗
        var showItem = new NativeMenuItem(loc["Tray_Show"]);
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Alert Actions
        var alertActionsItem = new NativeMenuItem(loc["Tray_AlertActions"]);
        alertActionsItem.Click += (_, _) => OpenAlertActions();
        menu.Items.Add(alertActionsItem);

        // Alert Groups
        var alertGroupsItem = new NativeMenuItem(loc["Tray_AlertGroups"]);
        alertGroupsItem.Click += (_, _) => OpenAlertGroups();
        menu.Items.Add(alertGroupsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // TD2-1: 開機啟動選項
        var startupItem = new NativeMenuItem(loc["Tray_StartWithWindows"]);
        startupItem.ToggleType = NativeMenuItemToggleType.CheckBox;
        startupItem.IsChecked = StartupManager.IsStartupEnabled();
        startupItem.Click += (_, _) =>
        {
            StartupManager.ToggleStartup();
            startupItem.IsChecked = StartupManager.IsStartupEnabled();
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // 退出
        var exitItem = new NativeMenuItem(loc["Tray_Exit"]);
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// 載入圖示
    /// </summary>
    private void LoadIcon()
    {
        if (_trayIcon == null) return;

        // 使用主視窗圖示 (從 AvaloniaResource 載入的 SendAlerts.ico)
        if (_mainWindow?.Icon != null)
        {
            _trayIcon.Icon = _mainWindow.Icon;
            return;
        }

        Log.Warning("[TrayIcon] 無法載入圖示，主視窗圖示為 null");
    }

    /// <summary>
    /// TD2-2: 最小化到系統匣
    /// </summary>
    public void MinimizeToTray()
    {
        if (_trayIcon == null || _mainWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow.Hide();
            _trayIcon.IsVisible = true;
            ShowTrayToast("SendAlerts", LocalizationService.Instance["Tray_MinimizedToast"]);
        });

        Log.Debug("[TrayIcon] 已最小化至系統匣");
    }

    /// <summary>
    /// 顯示短暫的 toast 通知視窗 (螢幕右下角)
    /// </summary>
    private static void ShowTrayToast(string title, string message, int durationMs = 2000)
    {
        var screen = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Screens.Primary
            : null;

        var toast = new Window
        {
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            Width = 300,
            Height = 60,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new StackPanel
            {
                Margin = new Thickness(12, 8),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeight.Bold,
                        FontSize = 13,
                        Foreground = Brushes.White
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
                    }
                }
            }
        };

        // 在 Show 前先定位到螢幕右下角
        if (screen != null)
        {
            var workArea = screen.WorkingArea;
            var scaling = screen.Scaling;
            var w = (int)(300 * scaling);
            var h = (int)(60 * scaling);
            toast.Position = new PixelPoint(
                workArea.Right - w - 10,
                workArea.Bottom - h - 10);
        }

        toast.Show();

        // 自動關閉
        Task.Delay(durationMs).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() => toast.Close()));
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

            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
            }
        });

        Log.Debug("[TrayIcon] 已顯示主視窗");
    }

    /// <summary>
    /// 隱藏系統匣圖示
    /// </summary>
    public void Hide()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    /// <summary>
    /// 顯示系統匣圖示
    /// </summary>
    public void Show()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = true;
        }
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OpenAlertActions()
    {
        ShowMainWindow();
    }

    private void OpenAlertGroups()
    {
        ShowMainWindow();
    }

    private void ExitApplication()
    {
        Log.Information("[TrayIcon] 使用者從系統匣選擇退出");

        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
            }
            _mainWindow?.Close();
            Environment.Exit(0);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
