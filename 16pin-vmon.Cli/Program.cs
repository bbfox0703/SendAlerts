using System.CommandLine;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using _16pin_vmon.Models;
using _16pin_vmon.Services;

namespace _16pin_vmon.Cli;

/// <summary>
/// TD1-1/TD1-2: 16pin-vmon CLI 工具
/// 用於發送警報和查詢群組清單
/// </summary>
class Program
{
    private const string PipeName = "16pin-vmon-alert";
    private const int DefaultTimeout = 3000;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("16pin-vmon CLI - Alert Center command line tool");

        // TD1-1: send command
        var sendCommand = CreateSendCommand();
        rootCommand.AddCommand(sendCommand);

        // TD1-2: list command
        var listCommand = CreateListCommand();
        rootCommand.AddCommand(listCommand);

        // test command (quick test)
        var testCommand = CreateTestCommand();
        rootCommand.AddCommand(testCommand);

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// TD1-1: 建立 send 命令
    /// Usage: 16pin-vmon-cli send -g Critical -m "GPU overheating!"
    /// </summary>
    private static Command CreateSendCommand()
    {
        var groupOption = new Option<string>(
            aliases: ["-g", "--group"],
            description: "Alert Group name to trigger")
        {
            IsRequired = true
        };

        var messageOption = new Option<string?>(
            aliases: ["-m", "--message"],
            description: "Custom message (optional)");

        var timeoutOption = new Option<int>(
            aliases: ["-t", "--timeout"],
            getDefaultValue: () => DefaultTimeout,
            description: "Connection timeout in milliseconds");

        var sendCommand = new Command("send", "Send an alert to 16pin-vmon")
        {
            groupOption,
            messageOption,
            timeoutOption
        };

        sendCommand.SetHandler(async (group, message, timeout) =>
        {
            await SendAlertAsync(group, message, timeout);
        }, groupOption, messageOption, timeoutOption);

        return sendCommand;
    }

    /// <summary>
    /// TD1-2: 建立 list 命令
    /// Usage: 16pin-vmon-cli list
    /// </summary>
    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "List available Alert Groups from settings");

        listCommand.SetHandler(() =>
        {
            ListGroups();
        });

        return listCommand;
    }

    /// <summary>
    /// 建立 test 命令 (快速測試)
    /// Usage: 16pin-vmon-cli test
    /// </summary>
    private static Command CreateTestCommand()
    {
        var testCommand = new Command("test", "Send a test alert to Default group");

        testCommand.SetHandler(async () =>
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
        Console.WriteLine($"[16pin-vmon-cli] Sending alert...");
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

        var jsonMessage = JsonSerializer.Serialize(pipeMessage, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            Console.WriteLine($"[16pin-vmon-cli] Connecting to pipe: \\\\.\\pipe\\{PipeName}");

            // Connect with timeout
            var cts = new CancellationTokenSource(timeout);
            await pipe.ConnectAsync(cts.Token);

            // Write message
            var bytes = Encoding.UTF8.GetBytes(jsonMessage);
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[16pin-vmon-cli] Alert sent successfully!");
            Console.ResetColor();
        }
        catch (OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[16pin-vmon-cli] ERROR: Connection timeout. Is 16pin-vmon running?");
            Console.ResetColor();
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[16pin-vmon-cli] ERROR: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// TD1-2: 列出設定檔中的群組
    /// </summary>
    private static void ListGroups()
    {
        Console.WriteLine("[16pin-vmon-cli] Loading settings...");

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
                Console.WriteLine("Tip: Start 16pin-vmon to initialize default groups.");
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
            Console.WriteLine($"[16pin-vmon-cli] ERROR: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
}
