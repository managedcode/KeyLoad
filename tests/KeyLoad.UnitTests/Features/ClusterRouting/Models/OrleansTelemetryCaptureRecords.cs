using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record OrleansActivityCapture(string SourceName, string DisplayName, string TraceId,
    string SpanId, string ParentSpanId, ActivityStatusCode Status, string? StatusDescription,
    string? TraceState, string[] Baggage, bool HasLinks, KeyValuePair<string, string?>[] Tags,
    OrleansActivityEventCapture[] Events);

internal sealed record OrleansActivityEventCapture(string Name, KeyValuePair<string, string?>[] Tags);

internal sealed record OrleansMetricPointCapture(string MeterName, string MetricName,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] ExemplarTags);

internal sealed record OrleansTelemetryOperationParent(ActivityTraceId TraceId, ActivitySpanId ParentSpanId);
