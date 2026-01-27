using System;
using System.Diagnostics;
using System.Threading.Tasks;
using _16pin_vmon.Core.Interfaces;
using Serilog;

namespace _16pin_vmon.Services;

/// <summary>
/// T4-1/TA2-1: 命令列警報動作 - 警報觸發時執行本機命令
/// 支援變數替換: {voltage}, {temperature}, {gpu_name}, {alert_type}
/// </summary>
public class CommandLineAlertAction : IAlertAction
{
    private readonly ISettingsService? _settingsService;
    private DateTime _lastExecutionTime = DateTime.MinValue;

    // TA2-1: 新增介面成員
    public string InstanceId { get; set; } = "CommandLine_Default";
    public AlertActionType ActionType => AlertActionType.CommandLine;
    public string DisplayName => string.IsNullOrWhiteSpace(InstanceId) ? "Command Line" : InstanceId;
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

    /// <summary>
    /// 建構子 - 從設定服務載入 (舊版相容)
    /// </summary>
    public CommandLineAlertAction(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();
    }

    /// <summary>
    /// 建構子 - 直接指定參數 (多實例支援)
    /// </summary>
    public CommandLineAlertAction(string instanceId, string command, int cooldownSeconds = 30, bool debugMode = false)
    {
        InstanceId = instanceId;
        Command = command;
        CooldownSeconds = cooldownSeconds;
        DebugMode = debugMode;
        IsEnabled = true;
    }

    private void LoadSettings()
    {
        if (_settingsService == null) return;
        var settings = _settingsService.Load();
        IsEnabled = settings.CommandLineAlertEnabled;
        Command = settings.CommandLineAlertCommand;
        CooldownSeconds = settings.CommandLineAlertCooldownSeconds;
        DebugMode = settings.AlertActionsDebugMode;
    }

    /// <summary>
    /// TA2-1: 驗證設定是否有效
    /// </summary>
    public AlertActionValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Command))
            return AlertActionValidationResult.Invalid("命令不可為空");

        return AlertActionValidationResult.Valid();
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
    public string SubstituteVariables(string command, float power, float temperature, string gpuName, string alertType)
    {
        return command
            .Replace("{power}", power.ToString("F1"))
            .Replace("{temperature}", temperature.ToString("F1"))
            .Replace("{gpu_name}", gpuName)
            .Replace("{alert_type}", alertType)
            .Replace("{timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
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
