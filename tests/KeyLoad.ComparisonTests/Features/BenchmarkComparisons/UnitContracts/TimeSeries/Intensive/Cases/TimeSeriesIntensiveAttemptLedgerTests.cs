using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveAttemptLedgerTests
{
    [Test]
    public async Task AcTsi004LedgerRejectsMissingDefaultDuplicateAndWrongIdentity()
    {
        var storage = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, 10000, 10000, 1);
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(default));
        var attempt = TimeSeriesIntensiveAttemptValues.Success(1, 0);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(attempt with { Repetition = 0 }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(attempt with { Worker = 1 }));
        ledger.Publish(attempt);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(attempt));
        await Assert.That(ledger.Attempts.Span[0]).IsEqualTo(attempt);
        await Assert.That(storage[0].Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
        await Assert.That(storage[49999].Index).IsEqualTo(9999);
        await Assert.That(storage[49999].Repetition).IsEqualTo(4);
    }

    [Test]
    public async Task AcTsi004CompletedLedgerKeepsFailedAttemptLatency()
    {
        var storage = new TimeSeriesIntensiveAttempt[256];
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, 0, 256, 3);
        for (var index = 0; index < 256; index++)
        {
            var attempt = TimeSeriesIntensiveAttemptValues.Success(3, index);
            ledger.Publish(index == 17 ? attempt with
            {
                Outcome = TimeSeriesIntensiveOutcome.TargetFailure,
                Failure = new(TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.OwnershipLost, 503, null)
            } : attempt);
        }

        ledger.ValidateComplete();
        await Assert.That(ledger.Succeeded).IsFalse();
        await Assert.That(ledger.Attempts.Span[17].LatencyTicks).IsEqualTo(18L);
        await Assert.That(ledger.Attempts.Span[17].Failure.KeyLoadCode).IsEqualTo(ErrorCode.OwnershipLost);
    }
}
