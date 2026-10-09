using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Observes only closed native host log identities under the original logging filters.</summary>
internal sealed class KeyLoadClientKestrelLogObservationProvider(KeyLoadClientKestrelObservation observation) : ILoggerProvider
{
    private const string Server = "Microsoft.AspNetCore.Server.Kestrel";
    private const string Connections = "Microsoft.AspNetCore.Server.Kestrel.Connections";
    private const string BadRequests = "Microsoft.AspNetCore.Server.Kestrel.BadRequests";
    private const string Http2 = "Microsoft.AspNetCore.Server.Kestrel.Http2";
    private const string Http3 = "Microsoft.AspNetCore.Server.Kestrel.Http3";
    private const string Sockets = "Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets";
    private const string Quic = "Microsoft.AspNetCore.Server.Kestrel.Transport.Quic";

    public ILogger CreateLogger(string categoryName)
        => new NativeLogger(observation, categoryName,
            categoryName is Server or Connections or BadRequests or Http2 or Http3 or Sockets or Quic);

    public void Dispose()
        => GC.SuppressFinalize(this);

    private sealed class NativeLogger(KeyLoadClientKestrelObservation observation, string category, bool admitted) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => admitted && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            { observation.RecordNativeServerEvent(category, eventId.Id, logLevel.ToString()); }
        }
    }
}
