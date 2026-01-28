using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    [STAThread]
    public static void Main(string[] args)
    {
        // T0-3: Initialize Serilog with file sink and rotation
        InitializeLogging();

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
                return;
            }

            // TA1-3: 啟動 Named Pipe Server
            InitializeNamedPipeServer();

            // T1-1: Initialize services based on platform
            InitializeServices();

            // TD2-2: 初始化系統匣圖示管理
            InitializeTrayIcon();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "應用程式發生致命錯誤");
            throw;
        }
        finally
        {
            // Cleanup
            ShutdownTrayIcon();
            ShutdownNamedPipeServer();
            ServiceLocator.GpuProvider?.Dispose();
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
        // TODO: TA1-2 實作 - 透過 Named Pipe 傳送參數給主實例
        // 目前僅顯示訊息並退出
        Log.Information("第二實例參數: {Args}", string.Join(" ", args));
        Console.WriteLine("SendAlerts 已在運行中。請使用 Named Pipe 發送指令。");
        Console.WriteLine($"Pipe 名稱: \\\\.\\pipe\\{SingleInstanceManager.NamedPipeName}");
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
    /// T1-1: 根據平台初始化服務
    /// </summary>
    private static void InitializeServices()
    {
        // Settings service (cross-platform)
        ServiceLocator.SettingsService = new JsonSettingsService();

        // GPU Provider (platform-specific)
        ServiceLocator.GpuProvider = CreateGpuProvider();

        Log.Information("GPU Provider: {ProviderType}, Available: {IsAvailable}",
            ServiceLocator.GpuProvider.GetType().Name,
            ServiceLocator.GpuProvider.IsAvailable);

        // TA3-2: Alert Service (Alert Center 核心)
        InitializeAlertService();
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
    /// 根據平台建立適當的 IGpuProvider
    /// 優先順序: NvAPI -> NVML -> CpuNetwork -> Demo
    /// </summary>
    private static IGpuProvider CreateGpuProvider()
    {
        // Windows: 嘗試使用 NvAPI (功耗 + 溫度)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 第一優先：嘗試 NvAPI (RTX 50 系列)
            try
            {
                var nvApiProvider = new NvApiWindowsProvider();
                if (nvApiProvider.IsAvailable)
                {
                    Log.Information("使用 NvAPI Windows Provider");
                    return nvApiProvider;
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

            // 第二優先：嘗試 NVML (舊版 NVIDIA GPU)
            try
            {
                var nvmlProvider = new NvmlWindowsProvider();
                if (nvmlProvider.IsAvailable)
                {
                    Log.Information("使用 NVML Windows Provider");
                    return nvmlProvider;
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

            // 第三優先：嘗試 CPU/Network (非 NVIDIA 系統 fallback)
            try
            {
                var cpuNetworkProvider = new CpuNetworkWindowsProvider();
                if (cpuNetworkProvider.IsAvailable)
                {
                    Log.Information("使用 CPU/Network Windows Provider (Fallback 模式)");
                    return cpuNetworkProvider;
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

        // Linux: 未來可實作 NvmlLinuxProvider
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Log.Information("Linux 平台 - 目前使用 Demo 模式 (NVML Linux 支援待實作)");
        }

        // Fallback: Demo Provider
        Log.Information("使用 Demo GPU Provider");
        return new DemoGpuProvider();
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
            .CreateLogger();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
