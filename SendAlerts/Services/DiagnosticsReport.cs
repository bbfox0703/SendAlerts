using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SendAlerts.Core.Interfaces;

namespace SendAlerts.Services;

/// <summary>
/// 產生系統診斷報告（純文字），供 DiagnosticsWindow 或 CLI 使用
/// </summary>
public static class DiagnosticsReport
{
    public static string Generate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SendAlerts Diagnostics ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // [System]
        sb.AppendLine("[System]");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($".NET Version: {Environment.Version}");
        var infoAttr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        sb.AppendLine($"App Version: {(infoAttr?.InformationalVersion is { } v ? $"v{v}" : "v?")}");
        sb.AppendLine();

        // [Hardware Provider]
        sb.AppendLine("[Hardware Provider]");
        var provider = ServiceLocator.GpuProvider;
        if (provider is not null)
        {
            var gpuName = provider.GetGpuName();
            sb.AppendLine($"Active Provider: {provider.GetType().Name}");

            var availableNames = new StringBuilder();
            foreach (var p in ServiceLocator.AvailableProviders)
            {
                if (availableNames.Length > 0) availableNames.Append(", ");
                availableNames.Append(p.GetType().Name);
            }
            if (availableNames.Length > 0)
                sb.AppendLine($"Available Providers: {availableNames}");

            sb.AppendLine($"Hardware Name: {gpuName}");
            sb.AppendLine($"Hardware Mode: {provider.Mode}");
            sb.AppendLine($"Power Limit (raw): {provider.PowerLimit:F1} {provider.SecondaryMetricUnit}");

            if (provider.Mode == HardwareMode.Gpu)
            {
                var tdp = GpuTdpLookup.FindTdp(gpuName);
                sb.AppendLine($"TDP Lookup: {(tdp.HasValue ? $"{tdp}W (matched)" : "no match")}");
            }
        }
        else
        {
            sb.AppendLine("Active Provider: (none)");
        }
        sb.AppendLine();

        // [Services]
        sb.AppendLine("[Services]");
        sb.AppendLine($"Named Pipe: {(ServiceLocator.IsPipeServerRunning ? "Running" : "Stopped")}");

        var httpApi = ServiceLocator.HttpApiServer;
        if (httpApi is not null)
            sb.AppendLine($"HTTP API: {(httpApi.IsRunning ? "Running" : "Stopped")}");
        else
            sb.AppendLine("HTTP API: Not configured");

        var alertService = ServiceLocator.AlertService;
        if (alertService is not null)
        {
            sb.AppendLine($"Alert Actions: {alertService.GetAllActions().Count} registered");
            sb.AppendLine($"Alert Groups: {alertService.GetAllGroups().Count} registered");
        }
        sb.AppendLine();

        // [Settings]
        sb.AppendLine("[Settings]");
        var settingsService = ServiceLocator.SettingsService;
        if (settingsService is not null)
        {
            sb.AppendLine($"Settings Path: {settingsService.GetSettingsFilePath()}");
            var settings = settingsService.Load();
            sb.AppendLine($"Sampling Interval: {settings.SamplingIntervalSeconds}s");
            sb.AppendLine($"Language: {settings.Language ?? "(auto)"}");
            sb.AppendLine($"HTTP API Enabled: {settings.HttpApiEnabled}");
            if (settings.HttpApiEnabled)
                sb.AppendLine($"HTTP API Port: {settings.HttpApiPort}");
            sb.AppendLine($"Watchdog Enabled: {settings.WatchdogEnabled}");
        }
        sb.AppendLine();

        // [Current Readings]
        if (provider is not null)
        {
            sb.AppendLine("[Current Readings]");
            try
            {
                var reading = provider.GetCurrentReading();
                sb.AppendLine($"{provider.PrimaryMetricLabel}: {reading.GpuUtilization:F1}%");
                sb.AppendLine($"{provider.TemperatureLabel}: {reading.Temperature:F1}{provider.TemperatureUnit}");
                sb.AppendLine($"{provider.SecondaryMetricLabel}: {reading.PowerUsage:F1}{provider.SecondaryMetricUnit}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(Failed to read: {ex.Message})");
            }
        }

        return sb.ToString();
    }
}
