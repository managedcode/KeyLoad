using System.Diagnostics;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record OrleansActivityCapture(string SourceName, string DisplayName, string TraceId,
    string SpanId, string ParentSpanId, ActivityStatusCode Status, string? StatusDescription,
    string? TraceState, string[] Baggage, bool HasLinks, KeyValuePair<string, string?>[] Tags,
    OrleansActivityEventCapture[] Events);
