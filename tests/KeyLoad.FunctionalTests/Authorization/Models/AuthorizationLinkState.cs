using System.Diagnostics;

namespace KeyLoad.FunctionalTests.Authorization;

internal sealed record AuthorizationLinkState(string TraceId, string SpanId, string? TraceState, bool HasTags, bool IsRemote, ActivityTraceFlags TraceFlags);
