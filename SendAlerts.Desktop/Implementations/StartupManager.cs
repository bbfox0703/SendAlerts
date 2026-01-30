using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SendAlerts.Interfaces;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// TD2-1: Windows 開機自動啟動管理器
/// 透過登錄檔設定開機啟動
/// </summary>
public class StartupManager : IStartupManager
{
    private const string AppName = "SendAlerts";
    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsStartupEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
            var value = key?.GetValue(AppName);
            return value != null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[StartupManager] 讀取開機啟動設定失敗");
            return false;
        }
    }

    public bool EnableStartup()
    {
        if (!IsSupported)
        {
            Log.Warning("[StartupManager] 僅支援 Windows 平台");
            return false;
        }

        try
        {
            var exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath))
            {
                Log.Warning("[StartupManager] 無法取得執行檔路徑");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key == null)
            {
                Log.Warning("[StartupManager] 無法開啟登錄檔");
                return false;
            }

            // 設定開機啟動，加入 --minimized 參數以最小化啟動
            key.SetValue(AppName, $"\"{exePath}\" --minimized");
            Log.Information("[StartupManager] 已啟用開機自動啟動: {Path}", exePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[StartupManager] 啟用開機啟動失敗");
            return false;
        }
    }

    public bool DisableStartup()
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key?.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, false);
                Log.Information("[StartupManager] 已停用開機自動啟動");
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[StartupManager] 停用開機啟動失敗");
            return false;
        }
    }

    public bool ToggleStartup()
    {
        if (IsStartupEnabled())
        {
            return DisableStartup();
        }
        else
        {
            return EnableStartup();
        }
    }

    private static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        // Fallback: 使用 AppContext，根據平台決定副檔名
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "SendAlerts.Desktop.exe"
            : "SendAlerts.Desktop";
        var appPath = Path.Combine(AppContext.BaseDirectory, exeName);

        return File.Exists(appPath) ? appPath : null;
    }
}
