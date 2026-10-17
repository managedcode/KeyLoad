namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record IsolatedAggregateNodePipeFailures(
    InvalidOperationException StandardOutput, InvalidOperationException StandardError, AggregateException Combined);
