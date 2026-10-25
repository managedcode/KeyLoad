using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Observes actual Microsoft logging EventSource formatted events for request failure diagnostics.</summary>
internal sealed class RequestFailureDiagnosticEventSourceCapture : EventListener
{
    private const string FilterSpecsKey = "FilterSpecs";
    private const string ServerErrorLoggerCategory = "KeyLoad.Server.ServerErrorMiddleware";
    private const string LoggerFilter = ServerErrorLoggerCategory + ":Error";
    private const int LoggerNamePayloadIndex = 2;
    private readonly ConcurrentQueue<string> messages = new();

    internal string Text => string.Join(" ", messages);

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Microsoft-Extensions-Logging")
        {
            var arguments = new Dictionary<string, string?> { [FilterSpecsKey] = LoggerFilter };
            EnableEvents(eventSource, EventLevel.Error, LoggingEventSource.Keywords.FormattedMessage, arguments);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == "FormattedMessage" && eventData.Payload is { } payload
                                                      && payload.Count > LoggerNamePayloadIndex
                                                      && payload[LoggerNamePayloadIndex] is string category
                                                      && string.Equals(category, ServerErrorLoggerCategory, StringComparison.Ordinal))
        {
            messages.Enqueue(string.Join(" ", payload));
        }
    }
}
