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
            ServiceLocator.GpuProvider?.Dispose();
            Log.Information("=== 16pin-vmon 應用程式結束 ===");
            Log.CloseAndFlush();
        }
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
