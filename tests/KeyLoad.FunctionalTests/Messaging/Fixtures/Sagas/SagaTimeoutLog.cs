using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class SagaTimeoutLog : ILoggerProvider
{
    private const int MaximumEntries = 100;
    private const int AssignedShardsEvent = 1545117348;
    private readonly ConcurrentQueue<string> entries = new();

    internal string Capture() => string.Join(Environment.NewLine, entries);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    private sealed class Logger(SagaTimeoutLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == AssignedShardsEvent)
            {
                return;
            }
            owner.entries.Enqueue($"{TimeProvider.System.GetUtcNow():O} {category} {eventId}: {formatter(state, exception)} {exception}");
            while (owner.entries.Count > MaximumEntries)
            {
                owner.entries.TryDequeue(out _);
            }
        }
    }
}
