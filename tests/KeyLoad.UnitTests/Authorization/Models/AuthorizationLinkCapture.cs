using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationLinkCapture(string TraceId, string SpanId, string? TraceState,
    bool IsRemote, ActivityTraceFlags TraceFlags, KeyValuePair<string, string?>[] Tags);
