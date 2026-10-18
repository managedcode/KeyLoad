namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record IsolatedAggregateNodePipeFailures(
    InvalidOperationException StandardOutput, InvalidOperationException StandardError, AggregateException Combined);
