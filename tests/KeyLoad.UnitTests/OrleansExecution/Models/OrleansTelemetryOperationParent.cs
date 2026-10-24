using System.Diagnostics;

namespace KeyLoad.UnitTests.OrleansExecution;

internal readonly record struct OrleansTelemetryOperationParent(ActivityTraceId TraceId, ActivitySpanId ParentSpanId);
