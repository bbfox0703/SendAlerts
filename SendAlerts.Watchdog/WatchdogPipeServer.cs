using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SendAlerts.Watchdog;

public class WatchdogPipeServer : IDisposable
{
    public const string PipeName = "sendalerts-watchdog-pipe";

    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event Action? ShutdownRequested;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = ListenAsync(_cts.Token);
        Log.Information("[WatchdogPipe] 開始監聽: {PipeName}", PipeName);
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var raw = await reader.ReadToEndAsync(ct);

                if (string.IsNullOrWhiteSpace(raw)) continue;

                Log.Debug("[WatchdogPipe] 收到訊息: {Raw}", raw);

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("Command", out var cmd) &&
                        cmd.GetString()?.Equals("Shutdown", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Log.Information("[WatchdogPipe] 收到 Shutdown 指令");
                        ShutdownRequested?.Invoke();
                        return;
                    }
                }
                catch (JsonException ex)
                {
                    Log.Warning(ex, "[WatchdogPipe] JSON 解析失敗");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[WatchdogPipe] 監聽錯誤");
                await Task.Delay(1000, ct);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listenTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
    }
}
