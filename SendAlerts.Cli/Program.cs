using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SendAlerts.Models;
using SendAlerts.Services;

namespace SendAlerts.Cli;

/// <summary>
/// TD1-1/TD1-2: SendAlerts CLI 工具
/// 用於發送警報和查詢群組清單
/// 注意：HWiNFO 呼叫時請使用 silent wrapper 腳本避免主控台視窗閃現
/// </summary>
class Program
{
    private const string PipeName = AppConstants.PipeName;
    private const int DefaultTimeout = 3000;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SendAlerts CLI - Alert Center command line tool");

        // TD1-1: send command
        var sendCommand = CreateSendCommand();
        rootCommand.Subcommands.Add(sendCommand);

        // TD1-2: list command
        var listCommand = CreateListCommand();
        rootCommand.Subcommands.Add(listCommand);

        // test command (quick test)
        var testCommand = CreateTestCommand();
        rootCommand.Subcommands.Add(testCommand);

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// TD1-1: 建立 send 命令
    /// Usage: SendAlerts-cli send -g Critical -m "GPU overheating!"
    /// </summary>
    private static Command CreateSendCommand()
    {
        var groupOption = new Option<string>("--group", "-g")
        {
            Description = "Alert Group name to trigger",
            Required = true
        };

        var messageOption = new Option<string?>("--message", "-m")
        {
            Description = "Custom message (optional)"
        };

        var timeoutOption = new Option<int>("--timeout", "-t")
        {
            Description = "Connection timeout in milliseconds",
            DefaultValueFactory = _ => DefaultTimeout
        };

        var sendCommand = new Command("send", "Send an alert to SendAlerts");
        sendCommand.Options.Add(groupOption);
        sendCommand.Options.Add(messageOption);
        sendCommand.Options.Add(timeoutOption);

        sendCommand.SetAction(async (parseResult, _) =>
        {
            var group = parseResult.GetValue(groupOption)!;
            var message = parseResult.GetValue(messageOption);
            var timeout = parseResult.GetValue(timeoutOption);
            await SendAlertAsync(group, message, timeout);
        });

        return sendCommand;
    }

    /// <summary>
    /// TD1-2: 建立 list 命令
    /// Usage: SendAlerts-cli list
    /// </summary>
    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "List available Alert Groups from settings");

        listCommand.SetAction(_ =>
        {
            ListGroups();
        });

        return listCommand;
    }

    /// <summary>
    /// 建立 test 命令 (快速測試)
    /// Usage: SendAlerts-cli test
    /// </summary>
    private static Command CreateTestCommand()
    {
        var testCommand = new Command("test", "Send a test alert to Default group");

        testCommand.SetAction(async (_, _) =>
        {
            await SendAlertAsync("Default", "[CLI Test] This is a test alert", DefaultTimeout);
        });

        return testCommand;
    }

    /// <summary>
    /// TD1-1: 發送警報到 Named Pipe
    /// </summary>
    private static async Task SendAlertAsync(string groupName, string? message, int timeout)
    {
        Console.WriteLine($"[SendAlerts-cli] Sending alert...");
        Console.WriteLine($"  Group: {groupName}");
        if (!string.IsNullOrEmpty(message))
        {
            Console.WriteLine($"  Message: {message}");
        }

        // Build JSON message
        var pipeMessage = new PipeMessage
        {
            GroupName = groupName,
            CustomMessage = message
        };

        var jsonMessage = JsonSerializer.Serialize(pipeMessage, CliJsonContext.Default.PipeMessage);

        // 嘗試連接，失敗時自動啟動 Desktop
        var connected = await TryConnectAndSendAsync(jsonMessage, timeout);

        if (!connected)
        {
            // 嘗試啟動 Desktop
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[SendAlerts-cli] SendAlerts not running, attempting to start...");
            Console.ResetColor();

            if (TryStartDesktop())
            {
                // 等待 Desktop 啟動並重試連接
                Console.WriteLine("[SendAlerts-cli] Waiting for SendAlerts to start...");
                await Task.Delay(3000); // 等待 3 秒讓 Desktop 啟動

                connected = await TryConnectAndSendAsync(jsonMessage, timeout);
            }
        }

        if (connected)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SendAlerts-cli] Alert sent successfully!");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[SendAlerts-cli] ERROR: Failed to send alert. Could not connect to SendAlerts.");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// 嘗試連接到 Named Pipe 並發送訊息
    /// </summary>
    private static async Task<bool> TryConnectAndSendAsync(string jsonMessage, int timeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            Console.WriteLine($"[SendAlerts-cli] Connecting to pipe: \\\\.\\pipe\\{PipeName}");

            // Connect with timeout
            var cts = new CancellationTokenSource(timeout);
            await pipe.ConnectAsync(cts.Token);

            // Write message
            var bytes = Encoding.UTF8.GetBytes(jsonMessage);
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[SendAlerts-cli] Connection failed: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    /// <summary>
    /// 嘗試啟動 Desktop 應用程式
    /// </summary>
    private static bool TryStartDesktop()
    {
        // 尋找同目錄下的 SendAlerts.Desktop.exe
        var cliPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(cliPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[SendAlerts-cli] ERROR: Cannot determine CLI path.");
            Console.ResetColor();
            return false;
        }

        var cliDir = Path.GetDirectoryName(cliPath);
        if (string.IsNullOrEmpty(cliDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[SendAlerts-cli] ERROR: Cannot determine CLI directory.");
            Console.ResetColor();
            return false;
        }

        var desktopExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "SendAlerts.Desktop.exe"
            : "SendAlerts.Desktop";
        var desktopPath = Path.GetFullPath(Path.Combine(cliDir, desktopExeName));

        // Path traversal protection: ensure resolved path stays within the CLI directory
        var resolvedDir = Path.GetDirectoryName(desktopPath);
        if (resolvedDir == null || !resolvedDir.Equals(Path.GetFullPath(cliDir), StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[SendAlerts-cli] ERROR: Desktop executable path is outside the expected directory.");
            Console.ResetColor();
            return false;
        }

        if (!File.Exists(desktopPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SendAlerts-cli] ERROR: SendAlerts.Desktop not found at: {desktopPath}");
            Console.ResetColor();
            return false;
        }

        try
        {
            Console.WriteLine($"[SendAlerts-cli] Starting: {desktopPath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = desktopPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized // 最小化啟動
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SendAlerts-cli] ERROR: Failed to start SendAlerts: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    /// <summary>
    /// TD1-2: 列出設定檔中的群組
    /// </summary>
    private static void ListGroups()
    {
        Console.WriteLine("[SendAlerts-cli] Loading settings...");

        try
        {
            var settingsService = new JsonSettingsService();
            var settings = settingsService.Load();

            Console.WriteLine();
            Console.WriteLine("=== Alert Groups ===");

            if (settings.AlertGroups.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  (No groups configured)");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Tip: Start SendAlerts to initialize default groups.");
                return;
            }

            foreach (var group in settings.AlertGroups)
            {
                var status = group.IsEnabled ? "Enabled" : "Disabled";
                var statusColor = group.IsEnabled ? ConsoleColor.Green : ConsoleColor.DarkGray;

                Console.Write("  - ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(group.Name);
                Console.ResetColor();
                Console.Write(" [");
                Console.ForegroundColor = statusColor;
                Console.Write(status);
                Console.ResetColor();
                Console.WriteLine("]");

                if (!string.IsNullOrEmpty(group.Description))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"      {group.Description}");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"      Actions: {group.ActionCount}");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("=== Alert Actions ===");

            if (settings.AlertActions.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  (No actions configured)");
                Console.ResetColor();
                return;
            }

            foreach (var action in settings.AlertActions)
            {
                var status = action.IsEnabled ? "Enabled" : "Disabled";
                var statusColor = action.IsEnabled ? ConsoleColor.Green : ConsoleColor.DarkGray;

                Console.Write("  - ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(action.InstanceId);
                Console.ResetColor();
                Console.Write($" ({action.ActionType}) [");
                Console.ForegroundColor = statusColor;
                Console.Write(status);
                Console.ResetColor();
                Console.WriteLine("]");
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SendAlerts-cli] ERROR: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PipeMessage))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
