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
    /// T1-1: 根據平台建立適當的 IGpuProvider
    /// 優先順序: NVML (有效電壓) -> NvAPI -> NVML (估算模式) -> Demo
    /// </summary>
    private static IGpuProvider CreateGpuProvider()
    {
        // Windows: 嘗試使用 NVML，若無法讀取電壓則 fallback 到 NvAPI
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            NvmlWindowsProvider? nvmlProvider = null;

            // 第一優先：嘗試 NVML
            try
            {
                nvmlProvider = new NvmlWindowsProvider();
                if (nvmlProvider.IsAvailable && !nvmlProvider.IsEstimatedVoltage)
                {
                    // NVML 可用且能讀取真實電壓
                    Log.Information("使用 NVML Windows Provider (直接電壓讀取)");
                    return nvmlProvider;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NVML 載入失敗");
                nvmlProvider?.Dispose();
                nvmlProvider = null;
            }

            // 第二優先：NVML 無法讀取電壓，嘗試 NvAPI
            if (nvmlProvider != null && nvmlProvider.IsEstimatedVoltage)
            {
                Log.Information("[Fallback] NVML 無法讀取電壓，嘗試 NvAPI...");

                try
                {
                    var nvApiProvider = new NvApiWindowsProvider();
                    if (nvApiProvider.IsAvailable)
                    {
                        // NvAPI 可用，釋放 NVML
                        nvmlProvider.Dispose();
                        Log.Information("使用 NvAPI Windows Provider");
                        return nvApiProvider;
                    }
                    else
                    {
                        Log.Warning("NvAPI 初始化失敗");
                        nvApiProvider.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NvAPI 載入失敗");
                }

                // NvAPI 也失敗，使用 NVML 估算模式
                Log.Information("使用 NVML Windows Provider (功耗估算模式)");
                return nvmlProvider;
            }

            // NVML 完全不可用，直接嘗試 NvAPI
            if (nvmlProvider == null || !nvmlProvider.IsAvailable)
            {
                nvmlProvider?.Dispose();

                try
                {
                    var nvApiProvider = new NvApiWindowsProvider();
                    if (nvApiProvider.IsAvailable)
                    {
                        Log.Information("使用 NvAPI Windows Provider (NVML 不可用)");
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
