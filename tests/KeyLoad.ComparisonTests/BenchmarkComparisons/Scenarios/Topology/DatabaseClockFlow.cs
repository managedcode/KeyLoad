using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class DatabaseClockFlow
{
    private const int ExpectedLoadedVectorCount = 256;

    internal static async Task VerifySessionReadAsync(IComparisonSession session, BenchmarkDocument document,
        DatabaseFlowTimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var timerCreations = timeProvider.TimerCreations;
        var actual = await session.ReadAsync(document, cancellationToken);
        await Assert.That(actual?.Json).IsEqualTo(document.Json);
        await Assert.That(timeProvider.TimerCreations).IsGreaterThan(timerCreations);
    }

    internal static async Task VerifyCancelledVectorReadAsync(IVectorComparisonTarget target,
        VectorComparisonProfile profile, IOptions<ComparisonExecutionOptions> executionOptions,
        DatabaseFlowTimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var timerCreations = timeProvider.TimerCreations;
        var corpus = new VectorComparisonCorpus(profile, executionOptions);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => target.ReadAsync(corpus.Create(0).Id, cancelled.Token));
        await Assert.That(failure).IsNotNull();
        await Assert.That(timeProvider.TimerCreations).IsGreaterThan(timerCreations);

        var rows = 0;
        await foreach (var _ in target.ReadbackAsync(cancellationToken).ConfigureAwait(false))
        {
            rows++;
        }
        await Assert.That(rows).IsEqualTo(ExpectedLoadedVectorCount);
    }
}
