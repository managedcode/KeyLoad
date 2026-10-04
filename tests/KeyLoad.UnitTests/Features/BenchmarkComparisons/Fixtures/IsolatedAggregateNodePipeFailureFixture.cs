namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodePipeFailureFixture
{
    internal static async Task<IsolatedAggregateNodePipeFailures> CaptureBothAsync(CancellationToken testToken)
    {
        await using var owner = new IsolatedAggregateNodePipeFailureOwner(testToken);
        return await owner.CaptureAsync();
    }
}

internal sealed record IsolatedAggregateNodePipeFailures(
    InvalidOperationException StandardOutput, InvalidOperationException StandardError, AggregateException Combined);
