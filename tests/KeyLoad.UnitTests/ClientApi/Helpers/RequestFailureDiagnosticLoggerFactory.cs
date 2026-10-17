using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventSource;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Keeps this native provider's closed category admission local to its owning factory.</summary>
internal static class RequestFailureDiagnosticLoggerFactory
{
    private const string Category = "KeyLoad.Server.ServerErrorMiddleware";

    internal static ILoggerFactory Create() => LoggerFactory.Create(builder => builder
        .AddEventSourceLogger()
        .AddFilter<EventSourceLoggerProvider>(Category, LogLevel.Error));
}
