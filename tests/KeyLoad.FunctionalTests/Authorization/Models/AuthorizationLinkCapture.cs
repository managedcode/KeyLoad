using System.Diagnostics;

namespace KeyLoad.FunctionalTests.Authorization;

internal sealed record AuthorizationLinkCapture(string TraceId, string SpanId, string? TraceState,
    bool IsRemote, ActivityTraceFlags TraceFlags, KeyValuePair<string, string?>[] Tags);
