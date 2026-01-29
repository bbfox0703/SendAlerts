using System;
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

    public ObservableCollection<string> LogEntries { get; } = new();

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

        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Add(message);
            while (LogEntries.Count > MaxEntries)
                LogEntries.RemoveAt(0);
        });
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(() => LogEntries.Clear());
    }
}
