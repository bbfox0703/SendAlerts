using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Serilog;

namespace SendAlerts.Watchdog;

public interface IProcessDetector
{
    bool IsTargetRunning();
    void LaunchTarget();
}

public class MutexProcessDetector : IProcessDetector
{
    private const string DesktopMutexName = "Local\\SendAlerts-single-instance";
    private const string DesktopExeName = "SendAlerts.Desktop.exe";

    public bool IsTargetRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(DesktopMutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Watchdog] Mutex 檢測失敗");
            return false;
        }
    }

    public void LaunchTarget()
    {
        try
        {
            var exePath = Path.Combine(AppContext.BaseDirectory, DesktopExeName);
            if (!File.Exists(exePath))
            {
                Log.Error("[Watchdog] 找不到 Desktop 執行檔: {Path}", exePath);
                return;
            }

            Log.Information("[Watchdog] 啟動 Desktop: {Path}", exePath);
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--minimized",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Watchdog] 啟動 Desktop 失敗");
        }
    }
}
