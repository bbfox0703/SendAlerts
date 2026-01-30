using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.Views;

/// <summary>
/// TD2-2: 主視窗 - 整合系統匣功能
/// </summary>
public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        // TD2-2: 視窗載入後通知系統匣管理器
        Loaded += OnWindowLoaded;

        // TD2-2: 攔截關閉事件，改為最小化到系統匣
        Closing += OnWindowClosing;
    }

    private void OnWindowLoaded(object? sender, EventArgs e)
    {
        // 通知 Desktop 專案的 TrayIconManager
        // 透過反射呼叫 Program.InitializeTrayIconWithWindow
        try
        {
            var desktopAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SendAlerts.Desktop");

            var programType = desktopAssembly?.GetType("SendAlerts.Desktop.Program");
            var method = programType?.GetMethod("InitializeTrayIconWithWindow",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            method?.Invoke(null, new object[] { this });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[MainWindow] 系統匣初始化跳過（選用功能）");
        }

        // TD2-1: 如果以最小化模式啟動，隱藏視窗
        if (ServiceLocator.StartMinimized)
        {
            Hide();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // 如果是強制關閉，則不攔截
        if (_forceClose)
        {
            return;
        }

        // TD2-2: 嘗試最小化到系統匣而非關閉
        if (ServiceLocator.MinimizeToTray != null)
        {
            e.Cancel = true;
            ServiceLocator.MinimizeToTray();
        }
    }

    /// <summary>
    /// 強制關閉視窗（從系統匣選單呼叫）
    /// </summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }
}
