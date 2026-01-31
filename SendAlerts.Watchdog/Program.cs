using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace SendAlerts.Watchdog;

sealed class Program
{
    private const string WatchdogMutexName = "Local\\SendAlerts-watchdog-instance";
    private static Mutex? _mutex;
    private static WatchdogPipeServer? _pipeServer;
    private static IProcessDetector _detector = new MutexProcessDetector();
    private static Timer? _checkTimer;
    private static TrayIcon? _trayIcon;
    private static CancellationTokenSource? _shutdownCts;

    [STAThread]
    public static void Main(string[] args)
    {
        InitializeLogging();

        try
        {
            Log.Information("=== SendAlerts Watchdog 啟動 ===");

            // Single instance check
            _mutex = new Mutex(true, WatchdogMutexName, out bool created);
            if (!created)
            {
                Log.Information("Watchdog 已在運行中，退出");
                return;
            }

            // Start pipe server for shutdown commands
            _pipeServer = new WatchdogPipeServer();
            _pipeServer.ShutdownRequested += OnShutdownRequested;
            _pipeServer.Start();

            // Start check timer (10 seconds)
            _checkTimer = new Timer(CheckDesktop, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

            _shutdownCts = new CancellationTokenSource();

            // Build Avalonia app with TrayIcon (no main window)
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Watchdog 發生致命錯誤");
        }
        finally
        {
            _checkTimer?.Dispose();
            _pipeServer?.Dispose();
            _shutdownCts?.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            Log.Information("=== SendAlerts Watchdog 結束 ===");
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Called after Avalonia app is initialized to set up TrayIcon.
    /// </summary>
    public static void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "SendAlerts Watchdog",
            Menu = new NativeMenu()
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "SendAlerts.ico");
        if (File.Exists(iconPath))
        {
            _trayIcon.Icon = new WindowIcon(iconPath);
        }

        var exitItem = new NativeMenuItem("Exit Watchdog");
        exitItem.Click += (_, _) =>
        {
            Log.Information("[Watchdog] 使用者從 TrayIcon 退出");
            ShutdownApp();
        };
        _trayIcon.Menu.Items.Add(exitItem);
        _trayIcon.IsVisible = true;
    }

    private static void ShutdownApp()
    {
        _shutdownCts?.Cancel();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    private static void CheckDesktop(object? state)
    {
        try
        {
            if (!_detector.IsTargetRunning())
            {
                Log.Information("[Watchdog] Desktop 未偵測到，正在重啟...");
                _detector.LaunchTarget();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Watchdog] 偵測/啟動 Desktop 時發生錯誤");
        }
    }

    private static void OnShutdownRequested()
    {
        Log.Information("[Watchdog] 收到 Desktop 關閉通知，Watchdog 即將退出");
        // Must dispatch to UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ShutdownApp());
    }

    private static void InitializeLogging()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SendAlerts",
            "logs");

        Directory.CreateDirectory(logDirectory);

        var logFilePath = Path.Combine(logDirectory, "Watchdog-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "SendAlerts.Watchdog")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 15,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<WatchdogApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
