using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Serilog;
using _16pin_vmon.Core.Interfaces;
using _16pin_vmon.Desktop.Implementations;
using _16pin_vmon.Implementations;
using _16pin_vmon.Services;

namespace _16pin_vmon.Desktop;

sealed class Program
{
    // TA1-1: Single Instance Manager
    private static SingleInstanceManager? _singleInstanceManager;

    // TA1-3: Named Pipe Server
    private static NamedPipeServer? _namedPipeServer;

    [STAThread]
    public static void Main(string[] args)
    {
        // T0-3: Initialize Serilog with file sink and rotation
        InitializeLogging();

        try
        {
            Log.Information("=== 16pin-vmon 應用程式啟動 ===");
            Log.Information("平台: {OS}, 架構: {Arch}",
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture);

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
            ShutdownNamedPipeServer();
            ServiceLocator.GpuProvider?.Dispose();
            _singleInstanceManager?.Dispose();
            Log.Information("=== 16pin-vmon 應用程式結束 ===");
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// TA1-2: 處理第二實例啟動 (未來透過 Named Pipe 傳送參數給主實例)
    /// </summary>
    private static void HandleSecondInstance(string[] args)
    {
        // TODO: TA1-2 實作 - 透過 Named Pipe 傳送參數給主實例
        // 目前僅顯示訊息並退出
        Log.Information("第二實例參數: {Args}", string.Join(" ", args));
        Console.WriteLine("16pin-vmon 已在運行中。請使用 Named Pipe 發送指令。");
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
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NamedPipe] 初始化 Named Pipe Server 失敗");
        }
    }

    /// <summary>
    /// TA1-3: 關閉 Named Pipe Server
    /// </summary>
    private static void ShutdownNamedPipeServer()
    {
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
    /// TA1-3: 處理 Named Pipe 收到的訊息
    /// </summary>
    private static void OnPipeMessageReceived(object? sender, PipeMessageReceivedEventArgs e)
    {
        // TODO: TA1-4/TA1-5 實作 - 解析 JSON 並觸發警報
        Log.Information("[NamedPipe] 處理訊息: {Message}", e.RawMessage);

        // 暫時只記錄訊息，等 TA1-4 (PipeMessage) 和 TA3 (AlertGroup) 實作後再處理
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
        // Cross-platform log path: %AppData%/16pin-vmon/logs (Windows) or ~/.config/16pin-vmon/logs (Linux)
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "16pin-vmon",
            "logs");

        Directory.CreateDirectory(logDirectory);

        var logFilePath = Path.Combine(logDirectory, "16pin-vmon-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "16pin-vmon")
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
