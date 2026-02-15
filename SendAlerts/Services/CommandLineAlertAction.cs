using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using SendAlerts.Core.Interfaces;
using Serilog;

namespace SendAlerts.Services;

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
    public int CooldownSeconds { get; set; } = AppConstants.DefaultCooldownSeconds;

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
    public CommandLineAlertAction(string instanceId, string command, int cooldownSeconds = AppConstants.DefaultCooldownSeconds, bool debugMode = false)
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

    public async Task<AlertActionExecuteResult> ExecuteAsync(string message)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(Command))
            return AlertActionExecuteResult.Skipped("動作已停用或命令為空");

        // 冷卻檢查
        var elapsed = DateTime.Now - _lastExecutionTime;
        if (elapsed.TotalSeconds < CooldownSeconds)
        {
            var msg = $"冷卻中，跳過執行 (剩餘 {CooldownSeconds - elapsed.TotalSeconds:F0} 秒)";
            Log.Debug("[CommandLineAlertAction] {Message}", msg);
            return AlertActionExecuteResult.Skipped(msg);
        }

        // Debug 模式：僅記錄
        if (DebugMode)
        {
            Log.Information("[CommandLineAlertAction][DEBUG MODE] 將執行命令: {Command}", Command);
            _lastExecutionTime = DateTime.Now;
            return AlertActionExecuteResult.Ok("[DEBUG] 模擬執行成功");
        }

        try
        {
            _lastExecutionTime = DateTime.Now;

            // Sanitize external message before substituting into shell command
            var sanitizedMessage = SanitizeShellInput(message);
            var finalCommand = Command.Replace("{message}", sanitizedMessage);

            Log.Information("[CommandLineAlertAction] Executing alert command: {Command}", finalCommand);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                Arguments = GetShellArguments(finalCommand),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            // 非同步等待完成
            var completed = await Task.Run(() => process.WaitForExit(AppConstants.CommandExecuteTimeoutMs));

            if (completed)
            {
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();

                if (process.ExitCode == 0)
                {
                    Log.Information("[CommandLineAlertAction] 命令執行成功 (ExitCode: 0)");
                    return AlertActionExecuteResult.Ok($"ExitCode: 0");
                }
                else
                {
                    var errMsg = string.IsNullOrWhiteSpace(stderr) ? "" : $" - {stderr.Trim()}";
                    Log.Warning("[CommandLineAlertAction] 命令執行失敗 (ExitCode: {ExitCode})", process.ExitCode);
                    return AlertActionExecuteResult.Fail($"ExitCode: {process.ExitCode}{errMsg}");
                }
            }
            else
            {
                var timeoutSec = AppConstants.CommandExecuteTimeoutMs / 1000;
                Log.Warning("[CommandLineAlertAction] 命令執行逾時 (超過 {Seconds} 秒)", timeoutSec);
                process.Kill();
                return AlertActionExecuteResult.Fail($"命令執行逾時 (超過 {timeoutSec} 秒)");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandLineAlertAction] 命令執行發生例外");
            return AlertActionExecuteResult.Fail($"例外: {ex.Message}");
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
            .Replace("{gpu_name}", SanitizeShellInput(gpuName))
            .Replace("{alert_type}", SanitizeShellInput(alertType))
            .Replace("{timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    /// <summary>
    /// 清理外部來源的變數值，移除 shell metacharacters 以防止命令注入
    /// </summary>
    internal static string SanitizeShellInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Shell metacharacters that could enable command injection
        var dangerous = new[] { '&', '|', ';', '$', '`', '>', '<', '(', ')', '{', '}', '\n', '\r' };
        var sanitized = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (Array.IndexOf(dangerous, c) < 0)
                sanitized.Append(c);
        }
        return sanitized.ToString();
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
