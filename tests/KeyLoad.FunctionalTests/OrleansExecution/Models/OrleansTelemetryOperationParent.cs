using System.Diagnostics;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal readonly record struct OrleansTelemetryOperationParent(ActivityTraceId TraceId, ActivitySpanId ParentSpanId);
