using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class NativeDatabaseClockFlow
{
    private const int ExpectedLoadedVectorCount = 256;

    internal static async Task VerifySessionReadAsync(IComparisonSession session, BenchmarkDocument document,
        NativeDatabaseFlowTimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var timerCreations = timeProvider.TimerCreations;
        var actual = await session.ReadAsync(document, cancellationToken);
        await Assert.That(actual?.Json).IsEqualTo(document.Json);
        await Assert.That(timeProvider.TimerCreations).IsGreaterThan(timerCreations);
    }

    internal static async Task VerifyCancelledVectorRunnerAsync(IVectorComparisonTarget target,
        VectorComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions,
        NativeDatabaseFlowTimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var utcNowReads = timeProvider.UtcNowReads;
        var runner = ComparisonRunner.ForVector(profile, executionOptions, provider: timeProvider);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => runner.RunAsync(target, null, cancelled.Token));
        await Assert.That(timeProvider.UtcNowReads).IsGreaterThan(utcNowReads);

        var rows = 0;
        await foreach (var _ in target.ReadbackAsync(cancellationToken).ConfigureAwait(false))
        {
            rows++;
        }
        await Assert.That(rows).IsEqualTo(ExpectedLoadedVectorCount);
    }
}
