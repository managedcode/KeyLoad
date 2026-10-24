using System.Diagnostics;

namespace KeyLoad.UnitTests.Authorization;

internal sealed record AuthorizationLinkCapture(string TraceId, string SpanId, string? TraceState,
    bool IsRemote, ActivityTraceFlags TraceFlags, KeyValuePair<string, string?>[] Tags);
