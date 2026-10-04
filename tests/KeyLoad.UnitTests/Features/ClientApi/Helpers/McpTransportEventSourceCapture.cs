using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Captures formatted events from the actual Microsoft logging EventSource provider.</summary>
internal sealed class McpTransportEventSourceCapture : EventListener
{
    private const string LoggingEventSourceName = "Microsoft-Extensions-Logging";
    private const string FilterSpecsKey = "FilterSpecs";
    private const string LoggerFilter = "GrainFailureDiagnosticsTests:Warning;McpTransportDiagnosticsTests:Warning";
    private const string FormattedMessageEvent = "FormattedMessage";
    private readonly ConcurrentQueue<string> messages = new();

    /// <summary>Gets formatted provider payloads observed during the capture.</summary>
    internal string Text => string.Join(" ", messages);

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
