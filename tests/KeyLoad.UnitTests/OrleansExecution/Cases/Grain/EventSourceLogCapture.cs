using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.UnitTests.OrleansExecution;

/// <summary>Observes the real Microsoft logging EventSource provider without replacing process output.</summary>
internal sealed class EventSourceLogCapture : EventListener
{
    private const string LoggingEventSourceName = "Microsoft-Extensions-Logging";
    private const string FilterSpecsKey = "FilterSpecs";
    private const string LoggerFilter = "GrainFailureDiagnosticsTests:Warning;McpTransportDiagnosticsTests:Warning;KeyLoad.Clustering.ConnectionGrain:Warning";
    private const string FormattedMessageEvent = "FormattedMessage";
    private const string LoggerNameField = "LoggerName";
    private readonly ConcurrentQueue<(string? Logger, string Row)> messages = new();

    /// <summary>Gets all formatted log event payload fields captured during the observation.</summary>
    internal string Text => string.Join(" ", messages.Select(message => message.Row));

    /// <summary>Gets complete original provider rows for request-specific correlation.</summary>
    internal IReadOnlyList<string> Messages => [.. messages.Select(message => message.Row)];

    /// <summary>Gets the rows one logger wrote. The provider is process-wide, so parallel tests' loggers also appear
    /// in <see cref="Text"/>.</summary>
    internal string TextFrom(string logger)
        => string.Join(" ", messages.Where(message => message.Logger == logger).Select(message => message.Row));

    /// <inheritdoc />
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == LoggingEventSourceName)
        {
            var arguments = new Dictionary<string, string?> { [FilterSpecsKey] = LoggerFilter };
            EnableEvents(eventSource, EventLevel.Warning, LoggingEventSource.Keywords.FormattedMessage, arguments);
        }
    }

    /// <inheritdoc />
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == FormattedMessageEvent && eventData.Payload is { } payload)
        {
            var logger = eventData.PayloadNames?.IndexOf(LoggerNameField) is >= 0 and var index
                ? payload[index] as string : null;
            messages.Enqueue((logger, string.Join(" ", payload)));
        }
    }
}
