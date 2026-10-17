using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationSpanCapture(string Source, string Operation, string Display,
    ActivityKind Kind, ActivityStatusCode Status, string? StatusDetail, string? TraceState,
    string TraceId, string SpanId, string ParentSpanId, TimeSpan Duration,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] Baggage,
    string[] Events, AuthorizationLinkCapture[] Links);
