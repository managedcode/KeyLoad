using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.Server.Authorization.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventSource;
using OpenTelemetry;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Keeps this execution provider's closed category admission local to its owning factory.</summary>
internal static class RequestFailureDiagnosticLoggerFactory
{
    private const string Category = "KeyLoad.Server.ServerErrorMiddleware";

    internal static ILoggerFactory Create(AuthorizationLogExporter? exporter = null) => LoggerFactory.Create(builder =>
    {
        builder.AddEventSourceLogger().AddFilter<EventSourceLoggerProvider>(Category, LogLevel.Error);
        if (exporter is not null)
        {
            builder.AddOpenTelemetry(options => options.AddProcessor(new RuntimeLogPrivacyProcessor())
                .AddProcessor(new SimpleLogRecordExportProcessor(exporter)));
        }
    });
}
