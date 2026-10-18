using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class SessionQueryChecksTarget : SessionChecksTarget, IBenchmarkSessionQueries
{
    internal int Queries;
    internal bool NativeIndexUnavailable
    {
        get; init;
    }
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return NativeIndexUnavailable
            ? Task.FromException(new BenchmarkSessionNativeCapabilityException("Contract-only native index absence."))
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
