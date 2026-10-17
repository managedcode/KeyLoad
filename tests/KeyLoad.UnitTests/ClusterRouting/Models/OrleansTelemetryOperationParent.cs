using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal readonly record struct OrleansTelemetryOperationParent(ActivityTraceId TraceId, ActivitySpanId ParentSpanId);
