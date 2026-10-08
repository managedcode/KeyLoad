using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationSpanCapture(string Source, string Operation, string Display,
    ActivityKind Kind, ActivityStatusCode Status, string? StatusDetail, string? TraceState,
    string TraceId, string SpanId, string ParentSpanId, TimeSpan Duration,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] Baggage,
    string[] Events, AuthorizationLinkCapture[] Links);
internal sealed record AuthorizationLogCapture(string? Category, string? Body, string? Formatted,
    int EventId, string? EventName, LogLevel Level, DateTime Timestamp, string TraceId, string SpanId,
    string? TraceState, string? Exception, KeyValuePair<string, string?>[] Attributes, string[] Scopes);
internal sealed record AuthorizationMetricCapture(string Meter, string Name, long Count, double Sum,
    KeyValuePair<string, string?>[] Tags);
internal sealed record AuthorizationSuppressionCapture(long Count, KeyValuePair<string, string?>[] Tags);
internal sealed record AuthorizationNativeSpanState(string Source, bool Recorded, string TraceId,
    string SpanId, string ParentSpanId, string[] Events, AuthorizationNativeLinkState[] Links,
    int LocalParentCount, string[] ParentOperations, KeyValuePair<string, string?>[] Baggage,
    KeyValuePair<string, string?>[] Tags, int MaximumParents, int MaximumBaggage, int MaximumTags);
internal sealed record AuthorizationNativeLinkState(string TraceId, string SpanId, string? TraceState, bool HasTags);

internal sealed record AuthorizationLinkCapture(string TraceId, string SpanId, string? TraceState,
    bool IsRemote, ActivityTraceFlags TraceFlags, KeyValuePair<string, string?>[] Tags);
