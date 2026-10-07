using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains closed native host failure metadata without formatting messages.</summary>
internal sealed class LocalRf3PreparationLogger(LocalRf3PreparationDiagnostics diagnostics) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new Capture(diagnostics, categoryName.StartsWith("Aspire.Hosting", StringComparison.Ordinal)
            ? "Aspire" : categoryName.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal)
                ? "Host" : null);
    public void Dispose() { }

    internal static string FailureKind(Exception? failure) => failure switch
    {
        OperationCanceledException => "Cancellation",
        TimeoutException => "Timeout",
        IOException => "IO",
        InvalidOperationException => "InvalidOperation",
        AggregateException => "Aggregate",
        null => "None",
        _ => "Other"
    };

    private sealed class Capture(LocalRf3PreparationDiagnostics diagnostics, string? category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => category is not null && logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                diagnostics.Record($"Native host category={category}; level={logLevel}; event={eventId.Id}; failure={FailureKind(exception)}");
            }
        }
    }
}
