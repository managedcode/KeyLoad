using System.Runtime.InteropServices;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveWarmupStorageTests
{
    private const int ExpectedRepetitionCount = 5;
    private const int ExpectedWarmupCount = 256;
    private const int ExpectedOperationCount = 10000;
    private const int ExpectedConcurrency = 16;
    private const int FailureIndex = 17;
    private const long NonOrdinalSequenceStart = 100003;
    private const long NonOrdinalSequenceStep = 31;
    private const long FailureLatency = 777;
    private const long FailureValidationLatency = 333;
    private const long FailureSequence = 91;
    private const string FailureSqlState = "40001";

    [Test]
    public async Task AcTw009001StorageInitializesAll1280CanonicalNotStartedAttempts()
    {
        var storage = TimeSeriesIntensiveAttemptLedger.CreateWarmupStorage();
        await Assert.That(storage.Length).IsEqualTo(ExpectedRepetitionCount * ExpectedWarmupCount);
        for (var slot = 0; slot < storage.Length; slot++)
        {
            var repetition = slot / ExpectedWarmupCount;
            var index = slot % ExpectedWarmupCount;
            var expected = new TimeSeriesIntensiveAttempt(repetition, index, index % ExpectedConcurrency,
                0, 0, TimeSeriesIntensiveOutcome.NotStarted, null, default, 0, default);
            await Assert.That(storage[slot]).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task AcTw009003SlicesRetainSuppliedActualFactsAndLeaveMeasuredStorageUntouched()
    {
        var warmupStorage = TimeSeriesIntensiveAttemptLedger.CreateWarmupStorage();
        var measuredStorage = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
        var measuredBefore = measuredStorage.ToArray();
        for (var repetition = 0; repetition < ExpectedRepetitionCount; repetition++)
        {
            var offset = repetition * ExpectedWarmupCount;
            var ledger = new TimeSeriesIntensiveAttemptLedger(warmupStorage, offset, ExpectedWarmupCount, repetition);
            for (var index = 0; index < ExpectedWarmupCount; index++)
            {
                var sequence = SequenceFor(repetition, index);
                ledger.Publish(SuppliedSuccess(repetition, index, sequence));
            }

            ledger.ValidateComplete();
        }

        for (var repetition = 0; repetition < ExpectedRepetitionCount; repetition++)
        {
            for (var index = 0; index < ExpectedWarmupCount; index++)
            {
                var slot = repetition * ExpectedWarmupCount + index;
                await Assert.That(warmupStorage[slot]).IsEqualTo(
                    SuppliedSuccess(repetition, index, SequenceFor(repetition, index)));
            }
        }

        await Assert.That(measuredStorage).IsEquivalentTo(measuredBefore, CollectionOrdering.Matching);
        await Assert.That(warmupStorage.Length).IsEqualTo(ExpectedRepetitionCount * ExpectedWarmupCount);
        await Assert.That(measuredStorage.Length).IsEqualTo(ExpectedRepetitionCount * ExpectedOperationCount);
    }

    [Test]
    public async Task AcTw009003WarmupLedgerRejectsMissingDuplicateAndWrongIdentityAndRetainsFailureFacts()
    {
        var storage = TimeSeriesIntensiveAttemptLedger.CreateWarmupStorage();
        var repetition = 2;
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, repetition * ExpectedWarmupCount,
            ExpectedWarmupCount, repetition);
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        var valid = SuppliedSuccess(repetition, 0, SequenceFor(repetition, 0));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(valid with { Repetition = 1 }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(valid with { Index = 1 }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(valid with { Worker = 1 }));

        var failed = TimeSeriesIntensiveAttemptValues.Success(repetition, FailureIndex) with
        {
            LatencyTicks = FailureLatency,
            ValidationTicks = FailureValidationLatency,
            ReceiptSequence = FailureSequence,
            Acknowledgement = new(CommandIdFor(repetition, FailureIndex), FailureSequence),
            Outcome = TimeSeriesIntensiveOutcome.TargetFailure,
            Failure = new(TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null,
                TimeSeriesIntensiveFailure.PackSqlState(FailureSqlState))
        };
        ledger.Publish(failed);
        Assert.ThrowsExactly<ComparisonFailureException>(() => ledger.Publish(failed));
        Assert.ThrowsExactly<ComparisonFailureException>(ledger.ValidateComplete);
        await Assert.That(storage[repetition * ExpectedWarmupCount + FailureIndex]).IsEqualTo(failed);
        await Assert.That(storage[repetition * ExpectedWarmupCount].Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
        await Assert.That(storage[repetition * ExpectedWarmupCount + FailureIndex].LatencyTicks).IsEqualTo(FailureLatency);
        await Assert.That(storage[repetition * ExpectedWarmupCount + FailureIndex].ValidationTicks).IsEqualTo(FailureValidationLatency);
        await Assert.That(storage[repetition * ExpectedWarmupCount + FailureIndex].ReceiptSequence).IsEqualTo(FailureSequence);
        await Assert.That(storage[repetition * ExpectedWarmupCount + FailureIndex].Acknowledgement)
            .IsEqualTo(failed.Acknowledgement);
        await Assert.That(storage[repetition * ExpectedWarmupCount + FailureIndex].Failure)
            .IsEqualTo(failed.Failure);
    }

    [Test]
    public async Task AcTw009002FinishExposesSameLiveOwnedWarmupViewWithoutCreatingSuccess()
    {
        var state = new TimeSeriesIntensiveRunState(TimeSeriesIntensiveScenario.Append);
        var ownedStorage = state.WarmupStorage;
        var result = state.Finish();

        await Assert.That(MemoryMarshal.TryGetArray(result.WarmupAttempts, out var segment)).IsTrue();
        await Assert.That(segment.Array).IsSameReferenceAs(ownedStorage);
        await Assert.That(result.WarmupAttempts.Length).IsEqualTo(ExpectedRepetitionCount * ExpectedWarmupCount);
        await Assert.That(result.WarmupAttempts.Span[0].Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
        await Assert.That(result.WarmupAttempts.Span[^1].Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
    }

    private static long SequenceFor(int repetition, int index)
        => NonOrdinalSequenceStart - ((repetition * ExpectedWarmupCount + index) * NonOrdinalSequenceStep);

    private static Guid CommandIdFor(int repetition, int index)
        => new(repetition + 1, (short)(index + 1), 1, 1, 2, 3, 4, 5, 6, 7, 8);

    private static TimeSeriesIntensiveAttempt SuppliedSuccess(int repetition, int index, long sequence)
        => TimeSeriesIntensiveAttemptValues.Success(repetition, index) with
        {
            ReceiptSequence = sequence,
            Acknowledgement = new(CommandIdFor(repetition, index), sequence)
        };
}
