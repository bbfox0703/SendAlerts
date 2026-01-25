using System;
using System.IO;
using Avalonia;
using Serilog;

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
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "應用程式發生致命錯誤");
            throw;
        }
        finally
        {
            Log.Information("=== 16pin-vmon 應用程式結束 ===");
            Log.CloseAndFlush();
        }
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
