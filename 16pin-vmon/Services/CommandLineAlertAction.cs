using System;
using System.Diagnostics;
using System.Threading.Tasks;
using _16pin_vmon.Core.Interfaces;
using Serilog;

namespace _16pin_vmon.Services;

/// <summary>
/// T4-1: 命令列警報動作 - 警報觸發時執行本機命令
/// 支援變數替換: {voltage}, {temperature}, {gpu_name}, {alert_type}
/// </summary>
public class CommandLineAlertAction : IAlertAction
{
    private readonly ISettingsService _settingsService;
    private DateTime _lastExecutionTime = DateTime.MinValue;

    public string ActionName => "CommandLine";
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 要執行的命令（支援變數替換）
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 命令執行冷卻時間（秒），避免重複執行
    /// </summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>
    /// Debug 模式：僅記錄不執行
    /// </summary>
    public bool DebugMode { get; set; } = false;

    public CommandLineAlertAction(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        IsEnabled = settings.CommandLineAlertEnabled;
        Command = settings.CommandLineAlertCommand;
        CooldownSeconds = settings.CommandLineAlertCooldownSeconds;
        DebugMode = settings.AlertActionsDebugMode;
    }

    public async Task ExecuteAsync(string message)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(Command))
        {
            return;
        }

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            Log.Debug("[CommandLineAlertAction] 冷卻中，跳過執行 (剩餘 {Remaining:F0} 秒)",
                CooldownSeconds - elapsed.TotalSeconds);
            return;
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[CommandLineAlertAction][DEBUG MODE] 將執行命令: {Command}", Command);
            _lastExecutionTime = DateTime.Now;
            return;
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            Log.Information("[CommandLineAlertAction] 執行警報命令: {Command}", Command);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                Arguments = GetShellArguments(Command),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            // 非同步等待完成（最多 30 秒）
            var completed = await Task.Run(() => process.WaitForExit(30000));

            if (completed)
            {
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();

                if (process.ExitCode == 0)
                {
                    Log.Information("[CommandLineAlertAction] 命令執行成功 (ExitCode: 0)");
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        Log.Debug("[CommandLineAlertAction] stdout: {Output}", stdout.Trim());
                    }
                }
                else
                {
                    Log.Warning("[CommandLineAlertAction] 命令執行失敗 (ExitCode: {ExitCode})",
                        process.ExitCode);
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        Log.Warning("[CommandLineAlertAction] stderr: {Error}", stderr.Trim());
                    }
                }
            }
            else
            {
                Log.Warning("[CommandLineAlertAction] 命令執行逾時 (超過 30 秒)");
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandLineAlertAction] 命令執行發生例外");
        }
    }

    /// <summary>
    /// 將命令中的變數替換為實際值
    /// </summary>
    public string SubstituteVariables(string command, float voltage, float temperature, string gpuName, string alertType)
    {
        return command
            .Replace("{voltage}", voltage.ToString("F3"))
            .Replace("{temperature}", temperature.ToString("F1"))
            .Replace("{gpu_name}", gpuName)
            .Replace("{alert_type}", alertType)
            .Replace("{timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    public void ShowConfigurationUI()
    {
        // T4-4: 將在 Alert Action Configuration UI 中實作
        Log.Debug("[CommandLineAlertAction] 設定 UI 尚未實作");
    }

    private static string GetShellExecutable()
    {
        return OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
    }

    private static string GetShellArguments(string command)
    {
        return OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"";
    }
}
