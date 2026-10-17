using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

/// <summary>Retains caller-supplied ledger facts without a warmup, repetition schedule or report schema.</summary>
internal sealed class TimeSeriesIntensiveAttemptOrderTests
{
    private const int SliceOffset = 1;
    private const int SliceCount = 3;
    private const int LedgerIdentity = 2;
    private const int SentinelIndex = 11;
    private const long FailureLatency = 777;
    private const long FailureValidationLatency = 333;
    private const long FailureSequence = 91;
    private const string FailureSqlState = "40001";

    [Test]
    public async Task SmallSliceStartsUnobservedAndPreservesCallerOwnedNeighbours()
    {
        var storage = Storage();
        var original = storage.ToArray();
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, SliceOffset, SliceCount, LedgerIdentity);
        for (var index = 0; index < SliceCount; index++)
        {
            var expected = new TimeSeriesIntensiveAttempt(LedgerIdentity, index, index,
                0, 0, TimeSeriesIntensiveOutcome.NotStarted, null, default, 0, default);
            await Assert.That(ledger.Attempts.Span[index]).IsEqualTo(expected);
        }
        await Assert.That(ledger.Attempts.Length).IsEqualTo(SliceCount);
        await Assert.That(ledger.Complete).IsFalse();
        await Assert.That(ledger.Succeeded).IsFalse();
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        await Assert.That(storage[0]).IsEqualTo(original[0]);
        await Assert.That(storage[^1]).IsEqualTo(original[^1]);
    }

    [Test]
    public async Task PublishedSliceRetainsSuppliedNonordinalSequencesAndCommandAcknowledgements()
    {
        var storage = Storage();
        var original = storage.ToArray();
        var unrelated = Storage();
        var unrelatedBefore = unrelated.ToArray();
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, SliceOffset, SliceCount, LedgerIdentity);
        var supplied = new[] { Supplied(0, 100003), Supplied(1, FailureSequence), Supplied(2, 9997) };
        foreach (var attempt in supplied)
        {
            ledger.Publish(attempt);
        }
        ledger.ValidateComplete();
        await Assert.That(ledger.Succeeded).IsTrue();
        for (var index = 0; index < supplied.Length; index++)
        {
            await Assert.That(ledger.Attempts.Span[index]).IsEqualTo(supplied[index]);
            await Assert.That(storage[SliceOffset + index]).IsEqualTo(supplied[index]);
        }
        await Assert.That(storage[0]).IsEqualTo(original[0]);
        await Assert.That(storage[^1]).IsEqualTo(original[^1]);
        await Assert.That(unrelated.SequenceEqual(unrelatedBefore)).IsTrue();
    }

    [Test]
    public async Task RejectedIdentityAndDuplicateCannotEraseAcknowledgedFailureFactsOrMissingSlots()
    {
        var storage = Storage();
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, SliceOffset, SliceCount, LedgerIdentity);
        var valid = Supplied(0, 100003);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(valid with { Repetition = 0 }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(valid with { Index = SliceCount }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(valid with { Worker = 1 }));
        var failed = Supplied(1, FailureSequence) with
        {
            LatencyTicks = FailureLatency,
            ValidationTicks = FailureValidationLatency,
            Outcome = TimeSeriesIntensiveOutcome.TargetFailure,
            Failure = new(TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null,
                TimeSeriesIntensiveFailure.PackSqlState(FailureSqlState))
        };
        ledger.Publish(failed);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(failed));
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        await Assert.That(storage[SliceOffset + failed.Index]).IsEqualTo(failed);
        await Assert.That(ledger.Attempts.Span[failed.Index].LatencyTicks).IsEqualTo(FailureLatency);
        await Assert.That(ledger.Attempts.Span[failed.Index].ValidationTicks).IsEqualTo(FailureValidationLatency);
        await Assert.That(ledger.Attempts.Span[failed.Index].ReceiptSequence).IsEqualTo(FailureSequence);
        await Assert.That(ledger.Attempts.Span[failed.Index].Acknowledgement).IsEqualTo(failed.Acknowledgement);
        await Assert.That(ledger.Attempts.Span[failed.Index].Failure).IsEqualTo(failed.Failure);
        await Assert.That(ledger.Attempts.Span[0].Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
        await Assert.That(ledger.Complete).IsFalse();
        await Assert.That(ledger.Succeeded).IsFalse();
    }

    private static TimeSeriesIntensiveAttempt[] Storage()
        => Enumerable.Repeat(TimeSeriesIntensiveAttemptValues.Success(LedgerIdentity, SentinelIndex),
            SliceOffset + SliceCount + SliceOffset).ToArray();

    private static TimeSeriesIntensiveAttempt Supplied(int index, long sequence)
        => TimeSeriesIntensiveAttemptValues.Success(LedgerIdentity, index) with
        {
            ReceiptSequence = sequence,
            Acknowledgement = new(new Guid(index + 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11), sequence)
        };
}
