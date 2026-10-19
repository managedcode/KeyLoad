using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class SessionQueryChecksTarget : SessionChecksTarget, IBenchmarkSessionQueries
{
    internal int Queries;
    internal bool RuntimeIndexUnavailable
    {
        get; init;
    }
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RuntimeIndexUnavailable
            ? Task.FromException(new BenchmarkSessionCapabilityException("Contract-only execution index absence."))
            : Task.CompletedTask;
    }
    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        if (!SynchronousOperations)
        {
            await Task.Yield();
        }
        cancellationToken.ThrowIfCancellationRequested();
        await ObserveMixedLaneAsync("search", cancellationToken);
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        Documents.TryGetValue(expected.Id, out var actual);
        BenchmarkSessionQueryContract.Require(actual is null ? null : new(actual.Id, actual.Json), expected);
        Interlocked.Increment(ref Queries);
    }
}
