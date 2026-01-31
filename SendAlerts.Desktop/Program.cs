using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;
using SendAlerts.Core.Interfaces;
using SendAlerts.Desktop.Implementations;
using SendAlerts.Implementations;
using SendAlerts.Models;
using SendAlerts.Services;

namespace SendAlerts.Desktop;

sealed class Program
{
    // TA1-1: Single Instance Manager
    private static SingleInstanceManager? _singleInstanceManager;

    // TA1-3: Named Pipe Server
    private static NamedPipeServer? _namedPipeServer;

    // TD2-2: Tray Icon Manager
    private static TrayIconManager? _trayIconManager;

    // HTTP API Server
    private static HttpApiServer? _httpApiServer;

    [STAThread]
    public static void Main(string[] args)
    {
        // T0-3: Initialize Serilog with file sink and rotation
        InitializeLogging();

        // 全域未處理例外攔截 — 確保 crash 前寫入 log
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is OperationCanceledException)
            {
                Log.Debug("應用程式關閉期間的取消例外");
            }
            else
            {
                Log.Fatal(e.ExceptionObject as Exception, "未處理的例外 (IsTerminating={IsTerminating})", e.IsTerminating);
            }
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // 關閉期間的取消例外不需記錄為錯誤
            if (e.Exception.InnerExceptions.All(ex => ex is OperationCanceledException or TaskCanceledException))
            {
                e.SetObserved();
                return;
            }
            Log.Error(e.Exception, "未觀察的 Task 例外");
            e.SetObserved();
        };

        try
        {
            Log.Information("=== SendAlerts 應用程式啟動 ===");
            Log.Information("平台: {OS}, 架構: {Arch}",
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture);

            // TD2-1: 檢查是否以最小化模式啟動
            ServiceLocator.StartMinimized = args.Contains("--minimized") || args.Contains("-m");
            if (ServiceLocator.StartMinimized)
            {
                Log.Information("以最小化模式啟動");
            }

            // TA1-1: 檢查單一實例
            _singleInstanceManager = new SingleInstanceManager();
            if (!_singleInstanceManager.TryAcquire())
            {
                // 已有實例運行，未來 TA1-2 會透過 Named Pipe 傳送參數
                Log.Information("偵測到已有實例運行，即將退出");
                HandleSecondInstance(args);
                Log.CloseAndFlush();
                return;
            }

            // TA1-3: 啟動 Named Pipe Server
            InitializeNamedPipeServer();

            // T1-1: Initialize services based on platform
            InitializeServices();

            // 初始化語系服務
            InitializeLocalization();

            // TD2-2: 初始化系統匣圖示管理
            InitializeTrayIcon();

            // 初始化 HTTP API 伺服器
            InitializeHttpApiServer();

            // Watchdog: 若啟用則連帶啟動
            LaunchWatchdogIfEnabled();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "應用程式發生致命錯誤");
            throw;
        }
        finally
        {
            // Cleanup — 每個步驟獨立 try-catch，確保不因清理失敗而中斷後續清理
            try { ShutdownWatchdogIfEnabled(); } catch (Exception ex) { Log.Warning(ex, "通知 Watchdog 關閉失敗"); }
            try { ShutdownHttpApiServer(); } catch (Exception ex) { Log.Warning(ex, "清理 HttpApiServer 失敗"); }
            try { ShutdownTrayIcon(); } catch (Exception ex) { Log.Warning(ex, "清理 TrayIcon 失敗"); }
            try { ShutdownNamedPipeServer(); } catch (Exception ex) { Log.Warning(ex, "清理 NamedPipeServer 失敗"); }
            try
            {
                foreach (var provider in ServiceLocator.AvailableProviders)
                {
                    provider.Dispose();
                }
                ServiceLocator.AvailableProviders.Clear();
            }
            catch (Exception ex) { Log.Warning(ex, "清理 GpuProviders 失敗"); }
            _singleInstanceManager?.Dispose();
            Log.Information("=== SendAlerts 應用程式結束 ===");
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// TD2-2: 初始化系統匣圖示
    /// </summary>
    private static void InitializeTrayIcon()
    {
        if (!TrayIconManager.IsSupported)
        {
            Log.Debug("[TrayIcon] 系統匣功能不支援此平台");
            return;
        }

        _trayIconManager = new TrayIconManager();

        // 註冊 ServiceLocator 委派
        ServiceLocator.MinimizeToTray = () => _trayIconManager?.MinimizeToTray();
        ServiceLocator.RestoreFromTray = () => _trayIconManager?.ShowMainWindow();
    }

    /// <summary>
    /// TD2-2: 在主視窗載入後完成系統匣初始化
    /// </summary>
    public static void InitializeTrayIconWithWindow(Window mainWindow)
    {
        _trayIconManager?.Initialize(mainWindow);

        // TD2-1: 如果以最小化模式啟動，則最小化到系統匣
        if (ServiceLocator.StartMinimized)
        {
            _trayIconManager?.MinimizeToTray();
        }
    }

    /// <summary>
    /// TD2-2: 關閉系統匣圖示
    /// </summary>
    private static void ShutdownTrayIcon()
    {
        _trayIconManager?.Dispose();
        _trayIconManager = null;
    }

    /// <summary>
    /// TA1-2: 處理第二實例啟動 (未來透過 Named Pipe 傳送參數給主實例)
    /// </summary>
    private static void HandleSecondInstance(string[] args)
    {
        Log.Information("第二實例參數: {Args}", string.Join(" ", args));

        // TA1-2: 透過 Named Pipe 通知主實例還原視窗
        try
        {
            using var pipe = new NamedPipeClientStream(".", SingleInstanceManager.NamedPipeName, PipeDirection.Out);
            pipe.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes("__restore");
            pipe.Write(bytes, 0, bytes.Length);
            Log.Information("已通知主實例還原視窗");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "無法連接主實例 Named Pipe");
            Console.WriteLine("SendAlerts 已在運行中，但無法通知主實例。");
        }
    }

    /// <summary>
    /// TA1-3: 初始化並啟動 Named Pipe Server
    /// </summary>
    private static void InitializeNamedPipeServer()
    {
        try
        {
            _namedPipeServer = new NamedPipeServer(SingleInstanceManager.NamedPipeName);

            // 註冊訊息接收事件
            _namedPipeServer.MessageReceived += OnPipeMessageReceived;
            _namedPipeServer.ErrorOccurred += OnPipeError;

            // 啟動伺服器
            _namedPipeServer.Start();

            // TB3-2: 更新 Pipe 伺服器狀態
            ServiceLocator.IsPipeServerRunning = _namedPipeServer.IsRunning;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NamedPipe] 初始化 Named Pipe Server 失敗");
            ServiceLocator.IsPipeServerRunning = false;
        }
    }

    /// <summary>
    /// TA1-3: 關閉 Named Pipe Server
    /// </summary>
    private static void ShutdownNamedPipeServer()
    {
        // TB3-2: 更新狀態
        ServiceLocator.IsPipeServerRunning = false;

        if (_namedPipeServer == null) return;

        try
        {
            // 使用同步方式等待停止 (在 finally 區塊中)
            _namedPipeServer.StopAsync().GetAwaiter().GetResult();
            _namedPipeServer.Dispose();
            _namedPipeServer = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NamedPipe] 關閉 Named Pipe Server 時發生錯誤");
        }
    }

    /// <summary>
    /// TA1-3/TA1-4: 處理 Named Pipe 收到的訊息
    /// </summary>
    private static void OnPipeMessageReceived(object? sender, PipeMessageReceivedEventArgs e)
    {
        // TA1-2: 第二實例要求還原視窗
        if (e.RawMessage?.Trim() == "__restore")
        {
            Log.Information("[NamedPipe] 收到 __restore 指令，還原主視窗");
            Dispatcher.UIThread.Post(() =>
            {
                if (ServiceLocator.RestoreFromTray is { } restore)
                {
                    restore();
                }
                else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                         && desktop.MainWindow is { } window)
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                }
            });
            return;
        }

        // TA1-4: 解析 JSON 訊息
        var parseResult = PipeMessageParser.Parse(e.RawMessage);

        if (!parseResult.Success)
        {
            // TA1-5: 錯誤處理
            Log.Warning("[NamedPipe] 訊息解析失敗: {Error} | ErrorType: {ErrorType} | Raw: {RawMessage}",
                parseResult.ErrorMessage,
                parseResult.ErrorType,
                e.RawMessage);
            return;
        }

        var message = parseResult.Message!;
        Log.Information("[NamedPipe] 收到有效訊息 | Group: {GroupName} | CustomMessage: {HasCustom}",
            message.GroupName,
            message.HasCustomMessage);

        // TA3-2: 執行 AlertService
        ExecuteAlertAsync(message);
    }

    /// <summary>
    /// TA3-2: 執行警報 (非同步，不阻塞 Pipe Server)
    /// </summary>
    private static async void ExecuteAlertAsync(PipeMessage message)
    {
        var alertService = ServiceLocator.AlertService;
        if (alertService == null)
        {
            Log.Warning("[NamedPipe] AlertService 未初始化，無法執行警報");
            return;
        }

        try
        {
            var result = await alertService.ExecuteAsync(message);

            if (result.Success)
            {
                Log.Information("[NamedPipe] 警報執行完成 | Group: {GroupName} | Executed: {Count}",
                    result.GroupName, result.ExecutedActions.Count);
            }
            else
            {
                Log.Warning("[NamedPipe] 警報執行失敗 | Group: {GroupName} | Error: {Error}",
                    result.GroupName, result.ErrorMessage ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NamedPipe] 執行警報時發生例外 | Group: {GroupName}", message.GroupName);
        }
    }

    /// <summary>
    /// TA1-3: 處理 Named Pipe 錯誤
    /// </summary>
    private static void OnPipeError(object? sender, PipeErrorEventArgs e)
    {
        Log.Warning(e.Exception, "[NamedPipe] Pipe 錯誤");
    }

    /// <summary>
    /// 初始化語系服務
    /// </summary>
    private static void InitializeLocalization()
    {
        var settings = ServiceLocator.SettingsService?.Load();
        var languageSetting = settings?.Language;

        LocalizationService.Instance.Initialize(languageSetting);
        Log.Information("語系初始化: {Culture}", LocalizationService.Instance.CurrentLanguageCode);
    }

    /// <summary>
    /// T1-1: 根據平台初始化服務
    /// </summary>
    private static void InitializeServices()
    {
        // Settings service (cross-platform)
        ServiceLocator.SettingsService = new JsonSettingsService();

        // Platform-specific managers
        ServiceLocator.StartupManager = new StartupManager();
        ServiceLocator.HttpUrlAclManager = new WindowsHttpUrlAclManager();

        // GPU Provider (platform-specific) - 建立所有可用 provider
        var providers = CreateAllProviders();
        ServiceLocator.AvailableProviders = providers;
        ServiceLocator.GpuProvider = providers[0];

        Log.Information("GPU Provider: {ProviderType}, Available: {IsAvailable}, Total Providers: {Count}",
            ServiceLocator.GpuProvider.GetType().Name,
            ServiceLocator.GpuProvider.IsAvailable,
            providers.Count);

        // TA3-2: Alert Service (Alert Center 核心)
        InitializeAlertService();
    }

    /// <summary>
    /// 初始化 HTTP API 伺服器
    /// </summary>
    private static void InitializeHttpApiServer()
    {
        var settings = ServiceLocator.SettingsService?.Load();
        if (settings == null || !settings.HttpApiEnabled)
        {
            Log.Debug("[HttpApiServer] HTTP API 未啟用");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.HttpApiKey))
        {
            Log.Warning("[HttpApiServer] HTTP API 已啟用但未設定 API Key，跳過初始化");
            return;
        }

        var alertService = ServiceLocator.AlertService;
        if (alertService == null)
        {
            Log.Warning("[HttpApiServer] AlertService 未初始化，無法啟動 HTTP API");
            return;
        }

        try
        {
            _httpApiServer = new HttpApiServer(alertService, settings.HttpApiPort, settings.HttpApiKey, ServiceLocator.HttpUrlAclManager);
            _httpApiServer.RequestProcessed += OnHttpApiRequestProcessed;
            _httpApiServer.Start();
            ServiceLocator.HttpApiServer = _httpApiServer;

            Log.Information("[HttpApiServer] HTTP API 已啟動，監聽 Port: {Port}", settings.HttpApiPort);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HttpApiServer] 啟動 HTTP API 失敗");
        }
    }

    /// <summary>
    /// 關閉 HTTP API 伺服器
    /// </summary>
    private static void ShutdownHttpApiServer()
    {
        if (_httpApiServer == null) return;

        try
        {
            _httpApiServer.Dispose();
            _httpApiServer = null;
            ServiceLocator.HttpApiServer = null;
            Log.Information("[HttpApiServer] HTTP API 已關閉");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HttpApiServer] 關閉時發生錯誤");
        }
    }

    /// <summary>
    /// 處理 HTTP API 請求事件
    /// </summary>
    private static void OnHttpApiRequestProcessed(object? sender, HttpApiRequestEventArgs e)
    {
        if (e.Success)
        {
            Log.Debug("[HttpApiServer] 請求成功 | IP: {IP} | Endpoint: {Endpoint} | Details: {Details}",
                e.RemoteIp, e.Endpoint, e.Details);
        }
        else
        {
            Log.Warning("[HttpApiServer] 請求失敗 | IP: {IP} | Endpoint: {Endpoint} | Details: {Details}",
                e.RemoteIp, e.Endpoint, e.Details);
        }
    }

    /// <summary>
    /// TA3-2/TA4-1: 初始化警報服務
    /// </summary>
    private static void InitializeAlertService()
    {
        var alertService = new AlertService();
        var settings = ServiceLocator.SettingsService?.Load();

        if (settings != null && settings.UseAlertCenterMode &&
            (settings.AlertActions.Count > 0 || settings.AlertGroups.Count > 0))
        {
            // TA4-1: 使用新版 Alert Center 設定
            alertService.LoadFromSettings(settings.AlertActions, settings.AlertGroups);
            Log.Information("AlertService 從設定檔載入 | Actions: {ActionCount}, Groups: {GroupCount}",
                alertService.ActionCount, alertService.GroupCount);
        }
        else
        {
            // 預設模式或首次啟動: 初始化預設群組
            alertService.InitializeDefaults();
            Log.Information("AlertService 使用預設配置 | Actions: {ActionCount}, Groups: {GroupCount}",
                alertService.ActionCount, alertService.GroupCount);
        }

        ServiceLocator.AlertService = alertService;
    }

    /// <summary>
    /// 建立所有可用的 IGpuProvider
    /// 優先順序: NvAPI -> NVML -> CpuNetwork -> Demo (保底)
    /// </summary>
    private static List<IGpuProvider> CreateAllProviders()
    {
        var providers = new List<IGpuProvider>();
        bool hasGpuProvider = false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // GPU Provider: NvAPI 與 NVML 為替代方案，只保留第一個成功的
            if (!hasGpuProvider)
            {
                try
                {
                    var nvApiProvider = new NvApiWindowsProvider();
                    if (nvApiProvider.IsAvailable)
                    {
                        providers.Add(nvApiProvider);
                        hasGpuProvider = true;
                        Log.Information("NvAPI Windows Provider 可用");
                    }
                    else
                    {
                        nvApiProvider.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NvAPI 載入失敗");
                }
            }

            if (!hasGpuProvider)
            {
                try
                {
                    var nvmlProvider = new NvmlWindowsProvider();
                    if (nvmlProvider.IsAvailable)
                    {
                        providers.Add(nvmlProvider);
                        hasGpuProvider = true;
                        Log.Information("NVML Windows Provider 可用");
                    }
                    else
                    {
                        nvmlProvider.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NVML 載入失敗");
                }
            }

            // CPU/Network: 與 GPU 為不同類別，可並存
            try
            {
                var cpuNetworkProvider = new CpuNetworkWindowsProvider();
                if (cpuNetworkProvider.IsAvailable)
                {
                    providers.Add(cpuNetworkProvider);
                    Log.Information("CPU/Network Windows Provider 可用");
                }
                else
                {
                    cpuNetworkProvider.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CPU/Network 載入失敗");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Log.Information("Linux 平台 - 目前使用 Demo 模式 (NVML Linux 支援待實作)");
        }

        // DemoGpuProvider 只在沒有任何可用 provider 時加入作為保底
        if (providers.Count == 0)
        {
            providers.Add(new DemoGpuProvider());
            Log.Information("無可用硬體 Provider，使用 Demo GPU Provider 作為保底");
        }

        Log.Information("共偵測到 {Count} 個可用 Provider", providers.Count);
        return providers;
    }

    private static void InitializeLogging()
    {
        // Cross-platform log path: %AppData%/SendAlerts/logs (Windows) or ~/.config/SendAlerts/logs (Linux)
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SendAlerts",
            "logs");

        Directory.CreateDirectory(logDirectory);

        var logFilePath = Path.Combine(logDirectory, "SendAlerts-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "SendAlerts")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .WriteTo.Sink(SendAlerts.Services.InMemoryLogSink.Instance)
            .CreateLogger();
    }

    /// <summary>
    /// Watchdog: 若啟用且未運行，啟動 Watchdog
    /// </summary>
    private static void LaunchWatchdogIfEnabled()
    {
        var settings = ServiceLocator.SettingsService?.Load();
        if (settings == null || !settings.WatchdogEnabled) return;

        const string watchdogMutexName = "Local\\SendAlerts-watchdog-instance";
        try
        {
            if (Mutex.TryOpenExisting(watchdogMutexName, out var existing))
            {
                existing.Dispose();
                Log.Information("[Watchdog] Watchdog 已在運行中");
                return;
            }

            var watchdogPath = Path.Combine(AppContext.BaseDirectory, "SendAlerts.Watchdog.exe");
            if (!File.Exists(watchdogPath))
            {
                Log.Warning("[Watchdog] 找不到 Watchdog 執行檔: {Path}", watchdogPath);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = watchdogPath,
                UseShellExecute = false
            });
            Log.Information("[Watchdog] 已啟動 Watchdog");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Watchdog] 啟動 Watchdog 失敗");
        }
    }

    /// <summary>
    /// Watchdog: 若設定需要，通知 Watchdog 關閉
    /// </summary>
    private static void ShutdownWatchdogIfEnabled()
    {
        var settings = ServiceLocator.SettingsService?.Load();
        if (settings == null || !settings.ShutdownWatchdogOnExit) return;

        const string watchdogMutexName = "Local\\SendAlerts-watchdog-instance";
        try
        {
            if (!Mutex.TryOpenExisting(watchdogMutexName, out var existing))
                return;
            existing.Dispose();

            using var client = new NamedPipeClientStream(".", "sendalerts-watchdog-pipe", PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            writer.Write("{\"Command\":\"Shutdown\"}");
            writer.Flush();
            Log.Information("[Watchdog] 已發送 Shutdown 指令給 Watchdog");
        }
        catch (TimeoutException)
        {
            Log.Debug("[Watchdog] 連線 Watchdog Pipe 逾時，可能已關閉");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Watchdog] 通知 Watchdog 關閉失敗");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
