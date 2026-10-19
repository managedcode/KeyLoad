using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed record OrleansActivityCapture(string SourceName, string DisplayName, string TraceId,
    string SpanId, string ParentSpanId, ActivityStatusCode Status, string? StatusDescription,
    string? TraceState, string[] Baggage, bool HasLinks, KeyValuePair<string, string?>[] Tags,
    OrleansActivityEventCapture[] Events);
