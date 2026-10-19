using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationLinkState(string TraceId, string SpanId, string? TraceState, bool HasTags, bool IsRemote, ActivityTraceFlags TraceFlags);
