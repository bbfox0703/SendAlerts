using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// TA1-3: Named Pipe Server
/// 持續監聽外部工具 (如 HWiNFO64) 傳送的警報觸發訊息
/// Pipe 名稱: \\.\pipe\sendalerts-pipe
/// </summary>
public sealed class NamedPipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts;
    private Task? _listenerTask;
    private bool _disposed;
    private int _isRunning;

    /// <summary>
    /// 當收到訊息時觸發
    /// </summary>
    public event EventHandler<PipeMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// 當發生錯誤時觸發
    /// </summary>
    public event EventHandler<PipeErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// 伺服器是否正在運行
    /// </summary>
    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    /// <summary>
    /// 建立 Named Pipe Server
    /// </summary>
    /// <param name="pipeName">Pipe 名稱 (不含 \\.\pipe\ 前綴)</param>
    public NamedPipeServer(string pipeName)
    {
        _pipeName = pipeName;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 啟動伺服器，開始監聽連線
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NamedPipeServer));

        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            Log.Warning("[NamedPipe] 伺服器已在運行中");
            return;
        }

        Log.Information("[NamedPipe] 啟動伺服器，監聽: \\\\.\\pipe\\{PipeName}", _pipeName);
        _listenerTask = ListenAsync(_cts.Token);
    }

    /// <summary>
    /// 停止伺服器
    /// </summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _isRunning, 0) == 0)
            return;

        Log.Information("[NamedPipe] 正在停止伺服器...");

        try
        {
            _cts.Cancel();

            // 建立一個假連線來解除 WaitForConnectionAsync 的阻塞
            await UnblockWaitingConnectionAsync().ConfigureAwait(false);

            if (_listenerTask != null)
            {
                await _listenerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            Log.Warning("[NamedPipe] 停止伺服器逾時");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NamedPipe] 停止伺服器時發生錯誤");
        }

        Log.Information("[NamedPipe] 伺服器已停止");
    }

    /// <summary>
    /// 主要監聽迴圈
    /// </summary>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;

            try
            {
                // 建立新的 Pipe 實例等待連線，使用 PipeSecurity 限制訪問
                var pipeSecurity = CreatePipeSecurity();
                pipeServer = NamedPipeServerStreamAcl.Create(
                    pipeName: _pipeName,
                    direction: PipeDirection.In,
                    maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity);

                Log.Debug("[NamedPipe] 等待連線...");

                // 等待客戶端連線
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }

                Log.Debug("[NamedPipe] 客戶端已連線");

                // 交給背景處理，主迴圈立即回去等待下一個連線
                var connectedPipe = pipeServer;
                pipeServer = null; // 避免 finally 中 dispose
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string message = await ReadMessageAsync(connectedPipe, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            Log.Information("[NamedPipe] 收到訊息: {Message}", message);
                            OnMessageReceived(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[NamedPipe] 處理連線時發生錯誤");
                    }
                    finally
                    {
                        connectedPipe.Dispose();
                    }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不需記錄錯誤
                Log.Debug("[NamedPipe] 監聽已取消");
            }
            catch (IOException ex)
            {
                // Pipe 斷開或其他 I/O 錯誤
                Log.Warning(ex, "[NamedPipe] I/O 錯誤");
                OnErrorOccurred(ex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NamedPipe] 監聽時發生未預期的錯誤");
                OnErrorOccurred(ex);

                // 避免錯誤時緊密循環
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                pipeServer?.Dispose();
            }
        }
    }

    /// <summary>
    /// 建立 Pipe 安全性設定 - 僅允許當前使用者訪問
    /// </summary>
    private static PipeSecurity CreatePipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();

        // 取得當前使用者的 SID
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            // 允許當前使用者完整控制
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            Log.Debug("[NamedPipe] Pipe security configured for user: {UserSid}", currentUser.Value);
        }

        // 明確拒絕 Everyone 群組的訪問（額外安全層）
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            everyoneSid,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return pipeSecurity;
    }

    /// <summary>
    /// 從 Pipe 讀取完整訊息
    /// </summary>
    private async Task<string> ReadMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        const int bufferSize = 4096;
        var buffer = new byte[bufferSize];
        var messageBuilder = new StringBuilder();

        try
        {
            int bytesRead;
            while ((bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuilder.Append(chunk);

                // 如果讀取的數據少於緩衝區大小，可能已經讀完
                // 但為了安全，我們檢查是否有更多數據
                if (bytesRead < bufferSize && !pipeServer.IsConnected)
                {
                    break;
                }

                // 簡單的訊息結束檢測：JSON 應該以 } 結尾
                string currentMessage = messageBuilder.ToString().Trim();
                if (currentMessage.EndsWith("}") || currentMessage.EndsWith("}\n"))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }

        return messageBuilder.ToString().Trim();
    }

    /// <summary>
    /// 解除 WaitForConnectionAsync 的阻塞
    /// </summary>
    private async Task UnblockWaitingConnectionAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await client.ConnectAsync(100).ConfigureAwait(false);
        }
        catch
        {
            // 忽略連線失敗，這只是為了解除阻塞
        }
    }

    private void OnMessageReceived(string rawMessage)
    {
        try
        {
            MessageReceived?.Invoke(this, new PipeMessageReceivedEventArgs(rawMessage));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NamedPipe] 處理訊息時發生錯誤");
        }
    }

    private void OnErrorOccurred(Exception exception)
    {
        try
        {
            ErrorOccurred?.Invoke(this, new PipeErrorEventArgs(exception));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[NamedPipe] 錯誤事件處理器本身發生錯誤");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>
/// Pipe 訊息接收事件參數
/// </summary>
public class PipeMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 原始訊息內容 (JSON 字串)
    /// </summary>
    public string RawMessage { get; }

    /// <summary>
    /// 訊息接收時間
    /// </summary>
    public DateTime ReceivedAt { get; }

    public PipeMessageReceivedEventArgs(string rawMessage)
    {
        RawMessage = rawMessage;
        ReceivedAt = DateTime.Now;
    }
}

/// <summary>
/// Pipe 錯誤事件參數
/// </summary>
public class PipeErrorEventArgs : EventArgs
{
    /// <summary>
    /// 發生的例外
    /// </summary>
    public Exception Exception { get; }

    public PipeErrorEventArgs(Exception exception)
    {
        Exception = exception;
    }
}
