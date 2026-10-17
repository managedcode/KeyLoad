using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveAttemptLedgerTests
{
    [Test]
    public async Task AcTsi004LedgerRejectsMissingDefaultDuplicateAndWrongIdentity()
    {
        var before = TimeSeriesIntensiveAttemptValues.Success(0, 31);
        var after = TimeSeriesIntensiveAttemptValues.Success(2, 47);
        TimeSeriesIntensiveAttempt[] storage = [before, default, default, after];
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, 1, 2, 1);
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(default));
        var attempt = TimeSeriesIntensiveAttemptValues.Success(1, 0);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(attempt with { Repetition = 0 }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(attempt with { Worker = 1 }));
        ledger.Publish(attempt);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(attempt));
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        await Assert.That(ledger.Attempts.Span[0]).IsEqualTo(attempt);
        await Assert.That(ledger.Attempts.Length).IsEqualTo(2);
        await Assert.That(storage[0]).IsEqualTo(before);
        await Assert.That(storage[2].Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
        await Assert.That(storage[3]).IsEqualTo(after);
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
