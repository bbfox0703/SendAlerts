using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SendAlerts.Interfaces;
using SendAlerts.Models;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// HTTP API 伺服器 - 提供遠端呼叫介面
/// </summary>
public class HttpApiServer : IDisposable
{
    private readonly AlertService _alertService;
    private readonly int _port;
    private readonly string _apiKey;
    private readonly IHttpUrlAclManager? _urlAclManager;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _disposed;

    // Track in-flight request handlers so Stop() can drain them before returning.
    private readonly ConcurrentDictionary<Task, byte> _inflight = new();
    private static readonly TimeSpan StopDrainTimeout = TimeSpan.FromSeconds(5);

    private const string ApiKeyHeader = "X-API-Key";
    private const long MaxRequestBodySize = 1024 * 1024; // 1 MB

    // Rate limiting: max requests per IP within the sliding window
    private const int RateLimitMaxRequests = 30;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, List<DateTime>> _requestLog = new();

    /// <summary>
    /// 伺服器是否正在運行
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 請求處理完成事件
    /// </summary>
    public event EventHandler<HttpApiRequestEventArgs>? RequestProcessed;

    public HttpApiServer(AlertService alertService, int port, string apiKey, IHttpUrlAclManager? urlAclManager = null)
    {
        _alertService = alertService ?? throw new ArgumentNullException(nameof(alertService));
        _port = port;
        _apiKey = apiKey;
        _urlAclManager = urlAclManager;
    }

    /// <summary>
    /// 啟動 HTTP 伺服器
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            Log.Warning("[HttpApiServer] Server is already running");
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Log.Error("[HttpApiServer] API Key is not configured");
            throw new InvalidOperationException("API Key is required");
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");

            _cts = new CancellationTokenSource();

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                // Access Denied — 嘗試自動註冊 URL ACL
                Log.Warning("[HttpApiServer] Access denied on http://+:{Port}/, attempting to register URL ACL...", _port);
                if (_urlAclManager != null && _urlAclManager.TryRegisterUrlAcl(_port))
                {
                    _listener.Start();
                }
                else
                {
                    // Fallback: 僅綁定 localhost（無法接受遠端連線）
                    Log.Warning("[HttpApiServer] URL ACL registration failed, falling back to localhost only");
                    _listener.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Start();
                }
            }

            IsRunning = true;
            _listenerTask = Task.Run(() => ListenAsync(_cts.Token));

            var prefix = _listener.Prefixes.FirstOrDefault() ?? "unknown";
            Log.Information("[HttpApiServer] Started on {Prefix}", prefix);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HttpApiServer] Failed to start");
            throw;
        }
    }

    /// <summary>
    /// 停止 HTTP 伺服器
    /// </summary>
    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _cts?.Cancel();
            _listener?.Stop();

            // Wait briefly for the accept loop and any in-flight handlers to finish so
            // handlers don't try to write to a closed response after Stop() returns.
            try
            {
                _listenerTask?.Wait(StopDrainTimeout);
                var pending = _inflight.Keys.ToArray();
                if (pending.Length > 0)
                {
                    Task.WaitAll(pending, StopDrainTimeout);
                }
            }
            catch (AggregateException)
            {
                // Handler exceptions are already logged inside ProcessRequestAsync; ignore here.
            }

            _listener?.Close();
            IsRunning = false;
            Log.Information("[HttpApiServer] Stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HttpApiServer] Error stopping server");
        }
    }

    /// <summary>
    /// 監聽請求
    /// </summary>
    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                var handler = Task.Run(() => ProcessRequestAsync(context), CancellationToken.None);
                _inflight.TryAdd(handler, 0);
                _ = handler.ContinueWith(t => _inflight.TryRemove(t, out _), TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995)
            {
                // Listener was stopped
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[HttpApiServer] Error accepting request");
            }
        }
    }

    /// <summary>
    /// 處理請求
    /// </summary>
    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var remoteIp = request.RemoteEndPoint?.Address?.ToString() ?? "unknown";

        Log.Debug("[HttpApiServer] Request from {IP}: {Method} {Path}",
            remoteIp, request.HttpMethod, request.Url?.AbsolutePath);

        try
        {
            // Rate limiting (skip for health check)
            var path = request.Url?.AbsolutePath ?? "/";
            if (path != "/api/health" && !CheckRateLimit(remoteIp))
            {
                Log.Warning("[HttpApiServer] Rate limit exceeded for {IP}", remoteIp);
                response.Headers.Add("Retry-After", "60");
                await SendJsonResponseAsync(response, (HttpStatusCode)429, new
                {
                    error = "Too Many Requests",
                    message = $"Rate limit exceeded. Maximum {RateLimitMaxRequests} requests per minute.",
                    retryAfterSeconds = 60
                });
                return;
            }

            // 路由
            if (path == "/api/send" && request.HttpMethod == "POST")
            {
                await HandleSendAsync(context, remoteIp);
            }
            else if (path == "/api/send" && request.HttpMethod == "GET")
            {
                // GET /api/send removed for security — API key in URL is unsafe
                Log.Warning("[HttpApiServer] Rejected GET /api/send from {IP} - use POST instead", remoteIp);
                response.Headers.Add("Allow", "POST");
                await SendJsonResponseAsync(response, HttpStatusCode.MethodNotAllowed, new
                {
                    error = "Method Not Allowed",
                    message = "GET /api/send is no longer supported. Use POST /api/send with X-API-Key header."
                });
            }
            else if (path == "/api/health" && request.HttpMethod == "GET")
            {
                await HandleHealthAsync(context);
            }
            else if (path == "/api/groups" && request.HttpMethod == "GET")
            {
                await HandleGroupsAsync(context, remoteIp);
            }
            else
            {
                await SendJsonResponseAsync(response, HttpStatusCode.NotFound, new
                {
                    error = "Not Found",
                    message = $"Endpoint not found: {path}"
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HttpApiServer] Error processing request");
            await SendJsonResponseAsync(response, HttpStatusCode.InternalServerError, new
            {
                error = "Internal Server Error",
                message = ex.Message
            });
        }
        finally
        {
            response.Close();
        }
    }

    /// <summary>
    /// 驗證 API Key（僅從 Header 讀取，不接受 Query String）
    /// </summary>
    private bool ValidateApiKey(HttpListenerRequest request, out string? providedKey)
    {
        providedKey = request.Headers[ApiKeyHeader];
        return !string.IsNullOrEmpty(providedKey) &&
               string.Equals(providedKey, _apiKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sliding window rate limiter per IP. Returns true if request is allowed.
    /// </summary>
    private bool CheckRateLimit(string remoteIp)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - RateLimitWindow;

        var timestamps = _requestLog.GetOrAdd(remoteIp, _ => new List<DateTime>());
        lock (timestamps)
        {
            // Remove expired entries
            timestamps.RemoveAll(t => t < cutoff);

            if (timestamps.Count >= RateLimitMaxRequests)
                return false;

            timestamps.Add(now);
            return true;
        }
    }

    /// <summary>
    /// 處理 POST /api/send
    /// </summary>
    private async Task HandleSendAsync(HttpListenerContext context, string remoteIp)
    {
        var request = context.Request;
        var response = context.Response;

        // 驗證 API Key
        if (!ValidateApiKey(request, out _))
        {
            Log.Warning("[HttpApiServer] Unauthorized request from {IP}", remoteIp);
            await SendJsonResponseAsync(response, HttpStatusCode.Unauthorized, new
            {
                error = "Unauthorized",
                message = "Invalid or missing API Key"
            });
            RaiseRequestProcessed(remoteIp, "send", false, "Unauthorized");
            return;
        }

        // 檢查 Content-Length 防止 DoS
        if (request.ContentLength64 > MaxRequestBodySize)
        {
            Log.Warning("[HttpApiServer] Request body too large from {IP}: {Size} bytes (max: {Max})",
                remoteIp, request.ContentLength64, MaxRequestBodySize);
            await SendJsonResponseAsync(response, HttpStatusCode.RequestEntityTooLarge, new
            {
                error = "Payload Too Large",
                message = $"Request body exceeds maximum size of {MaxRequestBodySize} bytes",
                maxSize = MaxRequestBodySize,
                receivedSize = request.ContentLength64
            });
            RaiseRequestProcessed(remoteIp, "send", false, "Payload too large");
            return;
        }

        // 讀取 Body（限制大小）
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            // 額外防護：使用 char buffer 限制讀取
            var buffer = new char[MaxRequestBodySize];
            var charsRead = await reader.ReadAsync(buffer, 0, (int)MaxRequestBodySize);
            body = new string(buffer, 0, charsRead);
        }

        // 解析 JSON
        HttpApiSendRequest? sendRequest;
        try
        {
            sendRequest = JsonSerializer.Deserialize<HttpApiSendRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            await SendJsonResponseAsync(response, HttpStatusCode.BadRequest, new
            {
                error = "Bad Request",
                message = $"Invalid JSON: {ex.Message}"
            });
            RaiseRequestProcessed(remoteIp, "send", false, "Invalid JSON");
            return;
        }

        if (sendRequest == null || string.IsNullOrWhiteSpace(sendRequest.GroupName))
        {
            await SendJsonResponseAsync(response, HttpStatusCode.BadRequest, new
            {
                error = "Bad Request",
                message = "groupName is required"
            });
            RaiseRequestProcessed(remoteIp, "send", false, "Missing groupName");
            return;
        }

        // 執行警報
        Log.Information("[HttpApiServer] Sending alert: Group={Group}, Message={Message}, From={IP}",
            sendRequest.GroupName, sendRequest.Message ?? "(none)", remoteIp);

        var result = await _alertService.ExecuteGroupAsync(sendRequest.GroupName, sendRequest.Message);

        if (result.Success)
        {
            await SendJsonResponseAsync(response, HttpStatusCode.OK, new
            {
                success = true,
                groupName = sendRequest.GroupName,
                executedActions = result.ExecutedActions.Count,
                message = "Alert sent successfully"
            });
            RaiseRequestProcessed(remoteIp, "send", true, sendRequest.GroupName);
        }
        else
        {
            await SendJsonResponseAsync(response, HttpStatusCode.BadRequest, new
            {
                success = false,
                error = result.ErrorMessage ?? "Failed to send alert",
                failedActions = result.FailedActions,
                missingActions = result.MissingActions
            });
            RaiseRequestProcessed(remoteIp, "send", false, result.ErrorMessage ?? "Failed");
        }
    }

    /// <summary>
    /// 處理 GET /api/health
    /// </summary>
    private async Task HandleHealthAsync(HttpListenerContext context)
    {
        await SendJsonResponseAsync(context.Response, HttpStatusCode.OK, new
        {
            status = "healthy"
        });
    }

    /// <summary>
    /// 處理 GET /api/groups (需要認證)
    /// </summary>
    private async Task HandleGroupsAsync(HttpListenerContext context, string remoteIp)
    {
        if (!ValidateApiKey(context.Request, out _))
        {
            Log.Warning("[HttpApiServer] Unauthorized request from {IP}", remoteIp);
            await SendJsonResponseAsync(context.Response, HttpStatusCode.Unauthorized, new
            {
                error = "Unauthorized",
                message = "Invalid or missing API Key"
            });
            return;
        }

        var groups = _alertService.GetAllGroups();
        var groupList = groups.Select(g => new
        {
            name = g.Name,
            description = g.Description,
            isEnabled = g.IsEnabled,
            actionCount = g.ActionCount
        });

        await SendJsonResponseAsync(context.Response, HttpStatusCode.OK, new
        {
            groups = groupList
        });
    }

    /// <summary>
    /// 發送 JSON 回應
    /// </summary>
    private static async Task SendJsonResponseAsync(HttpListenerResponse response, HttpStatusCode statusCode, object data)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.Add("Access-Control-Allow-Origin", "http://localhost");

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    private void RaiseRequestProcessed(string remoteIp, string endpoint, bool success, string details)
    {
        RequestProcessed?.Invoke(this, new HttpApiRequestEventArgs(remoteIp, endpoint, success, details));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}

/// <summary>
/// HTTP API 發送請求模型
/// </summary>
public class HttpApiSendRequest
{
    public string GroupName { get; set; } = string.Empty;
    public string? Message { get; set; }
}

/// <summary>
/// HTTP API 請求事件參數
/// </summary>
public class HttpApiRequestEventArgs : EventArgs
{
    public string RemoteIp { get; }
    public string Endpoint { get; }
    public bool Success { get; }
    public string Details { get; }
    public DateTime Timestamp { get; }

    public HttpApiRequestEventArgs(string remoteIp, string endpoint, bool success, string details)
    {
        RemoteIp = remoteIp;
        Endpoint = endpoint;
        Success = success;
        Details = details;
        Timestamp = DateTime.Now;
    }
}
