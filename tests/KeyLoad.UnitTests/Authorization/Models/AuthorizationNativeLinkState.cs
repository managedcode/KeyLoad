using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationNativeLinkState(string TraceId, string SpanId, string? TraceState, bool HasTags, bool IsRemote, ActivityTraceFlags TraceFlags);
