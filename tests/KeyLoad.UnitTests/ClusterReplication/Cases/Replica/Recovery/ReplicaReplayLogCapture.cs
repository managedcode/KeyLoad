using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaReplayLogCapture : ILoggerProvider
{
    internal ConcurrentQueue<ReplicaReplayLogEntry> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
    public void Dispose()
    {
    }

    private sealed class CaptureLogger(ConcurrentQueue<ReplicaReplayLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(
                item => item.Key, item => item.Value, StringComparer.Ordinal);
            entries.Enqueue(new(eventId.Id, fields, formatter(state, exception)));
        }
    }
}
