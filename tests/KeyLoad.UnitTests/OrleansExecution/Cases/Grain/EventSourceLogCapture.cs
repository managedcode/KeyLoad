using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Observes the real Microsoft logging EventSource provider without replacing process output.</summary>
internal sealed class EventSourceLogCapture : EventListener
{
    private const string LoggingEventSourceName = "Microsoft-Extensions-Logging";
    private const string FilterSpecsKey = "FilterSpecs";
    private const string LoggerFilter = "GrainFailureDiagnosticsTests:Warning;McpTransportDiagnosticsTests:Warning;KeyLoad.Clustering.ConnectionGrain:Warning";
    private const string FormattedMessageEvent = "FormattedMessage";
    private readonly ConcurrentQueue<string> messages = new();

    /// <summary>Gets all formatted log event payload fields captured during the observation.</summary>
    internal string Text => string.Join(" ", messages);

    /// <summary>Gets complete original provider rows for request-specific correlation.</summary>
    internal IReadOnlyList<string> Messages => messages.ToArray();

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
            messages.Enqueue(string.Join(" ", payload));
        }
    }
}
