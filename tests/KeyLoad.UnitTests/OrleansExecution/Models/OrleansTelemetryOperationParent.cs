using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal readonly record struct OrleansTelemetryOperationParent(ActivityTraceId TraceId, ActivitySpanId ParentSpanId);
