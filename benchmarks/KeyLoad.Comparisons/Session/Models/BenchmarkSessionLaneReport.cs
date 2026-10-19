using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>A bounded execution operation stream; timestamps share the enclosing case's monotonic origin.</summary>
public sealed record BenchmarkSessionLaneReport(string Id, Measurement Measurement,
    double FirstStartedMs, double LastCompletedMs, ImmutableArray<BenchmarkSessionOperationSample> Samples);
