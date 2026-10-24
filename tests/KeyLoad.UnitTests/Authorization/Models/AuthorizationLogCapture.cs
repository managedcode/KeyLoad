using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Authorization;

internal sealed record AuthorizationLogCapture(string? Category, string? Body, string? Formatted,
    int EventId, string? EventName, LogLevel Level, DateTime Timestamp, string TraceId, string SpanId,
    string? TraceState, string? Exception, KeyValuePair<string, string?>[] Attributes, string[] Scopes);
