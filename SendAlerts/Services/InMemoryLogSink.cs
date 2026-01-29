using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;

namespace SendAlerts.Services;

public class InMemoryLogSink : ILogEventSink
{
    public static readonly InMemoryLogSink Instance = new();

    private const int MaxEntries = 500;
    private const string OutputTemplate = "[{0:HH:mm:ss} {1}] {2}";

    private volatile bool _dispatcherReady;
    private readonly ConcurrentQueue<string> _pendingMessages = new();

    public ObservableCollection<string> LogEntries { get; } = new();

    /// <summary>
    /// Call after Avalonia is initialized to flush buffered messages and enable UI dispatching.
    /// </summary>
    public void MarkDispatcherReady()
    {
        _dispatcherReady = true;
        FlushPending();
    }

    public void Emit(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "???"
        };

        var message = string.Format(OutputTemplate, logEvent.Timestamp, level, logEvent.RenderMessage());

        if (logEvent.Exception != null)
            message += Environment.NewLine + logEvent.Exception;

        if (_dispatcherReady)
        {
            Dispatcher.UIThread.Post(() => AddMessage(message));
        }
        else
        {
            _pendingMessages.Enqueue(message);
        }
    }

    public void Clear()
    {
        if (_dispatcherReady)
            Dispatcher.UIThread.Post(() => LogEntries.Clear());
    }

    private void FlushPending()
    {
        Dispatcher.UIThread.Post(() =>
        {
            while (_pendingMessages.TryDequeue(out var msg))
            {
                AddMessage(msg);
            }
        });
    }

    private void AddMessage(string message)
    {
        LogEntries.Add(message);
        while (LogEntries.Count > MaxEntries)
            LogEntries.RemoveAt(0);
    }
}
