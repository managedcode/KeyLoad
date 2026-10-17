using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record OrleansActivityCapture(string SourceName, string DisplayName, string TraceId,
    string SpanId, string ParentSpanId, ActivityStatusCode Status, string? StatusDescription,
    string? TraceState, string[] Baggage, bool HasLinks, KeyValuePair<string, string?>[] Tags,
    OrleansActivityEventCapture[] Events);

internal sealed record OrleansActivityEventCapture(string Name, KeyValuePair<string, string?>[] Tags);

internal readonly record struct OrleansMetricPointCapture(string MeterName, string MetricName,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] ExemplarTags);

internal readonly record struct OrleansTelemetryOperationParent(ActivityTraceId TraceId, ActivitySpanId ParentSpanId);
