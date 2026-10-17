using System.Collections.Immutable;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseBankTests
{
    private const int PhaseCount = 32;
    private const int OutcomeCount = 6;
    private const int BucketCount = 16;
    private const int HistogramLength = PhaseCount * OutcomeCount * BucketCount;
    private const long DisabledTimestamp = -1;
    private const int InvalidPhase = -1;
    private const int InvalidOutcome = -1;
    private const long InvalidElapsed = -2;
    private const long FutureTimestamp = long.MaxValue;

    [Test]
    public async Task DisabledBankReturnsEmptyUnavailableSnapshotWithoutRecording()
    {
        var bank = DatabasePhaseTestComposition.Create(false);
        var started = bank.Begin();
        bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, started);
        bank.End((DatabasePhaseKind)InvalidPhase, (DatabasePhaseOutcome)OutcomeCount, DisabledTimestamp);
        bank.RecordBusy((DatabasePhaseKind)PhaseCount);
        bank.RecordBusy(DatabasePhaseKind.NativeTreeMutation);
        var snapshot = bank.Capture();

        await Assert.That(bank.IsEnabled).IsFalse();
        await Assert.That(started).IsEqualTo(DisabledTimestamp);
        await Assert.That(snapshot.Enabled).IsFalse();
        await Assert.That(snapshot.Frequency).IsEqualTo(0L);
        await Assert.That(snapshot.Started).IsEqualTo(0L);
        await Assert.That(snapshot.Finished).IsEqualTo(0L);
        await Assert.That(snapshot.Histogram.IsDefault).IsFalse();
        await Assert.That(snapshot.Histogram.IsEmpty).IsTrue();
        await Assert.That(snapshot.BusyAttempts.IsDefault).IsFalse();
        await Assert.That(snapshot.BusyAttempts.IsEmpty).IsTrue();
        await Assert.That(snapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
    }

    [Test]
    public async Task EnabledBankStartsWithTheExactEmptyFixedSchema()
    {
        var bank = DatabasePhaseTestComposition.Create(true);
        var snapshot = bank.Capture();

        await Assert.That(bank.IsEnabled).IsTrue();
        await Assert.That(snapshot.Enabled).IsTrue();
        await Assert.That(snapshot.Frequency).IsGreaterThan(0L);
        await Assert.That(snapshot.Finished).IsGreaterThanOrEqualTo(snapshot.Started);
        await Assert.That(snapshot.Histogram.Length).IsEqualTo(HistogramLength);
        await Assert.That(snapshot.BusyAttempts.Length).IsEqualTo(PhaseCount);
        await Assert.That(snapshot.Histogram.All(static value => value == 0)).IsTrue();
        await Assert.That(snapshot.BusyAttempts.All(static value => value == 0)).IsTrue();
        await Assert.That(snapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
    }

    [Test]
    [Arguments(1, 1)]
    [Arguments(1, 2)]
    [Arguments(1, 3)]
    [Arguments(1, 4)]
    [Arguments(2, 1)]
    [Arguments(2, 2)]
    [Arguments(2, 3)]
    [Arguments(2, 4)]
    [Arguments(4, 1)]
    [Arguments(4, 2)]
    [Arguments(4, 3)]
    [Arguments(4, 4)]
    public async Task RealBeginEndAndBusyUpdatesUseIndependentFixedLanes(int stripeCount, int maximumCasAttempts)
    {
        var bank = DatabasePhaseTestComposition.Create(true, stripeCount, maximumCasAttempts);
        var kind = DatabasePhaseKind.ProviderWriteGateHold;
        var started = bank.Begin();
        bank.End(kind, DatabasePhaseOutcome.Completed, started);
        bank.RecordBusy(kind);
        var snapshot = bank.Capture();
        var phaseIndex = (int)kind;
        var laneStart = (phaseIndex * OutcomeCount + (int)DatabasePhaseOutcome.Completed) * BucketCount;

        await Assert.That(snapshot.Histogram.Length).IsEqualTo(HistogramLength);
        await Assert.That(LaneTotal(snapshot.Histogram, laneStart)).IsEqualTo(1L);
        await Assert.That(Total(snapshot.BusyAttempts)).IsEqualTo(1L);
        await Assert.That(snapshot.BusyAttempts[phaseIndex]).IsEqualTo(1L);
        await Assert.That(snapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
    }

    [Test]
    public async Task EveryClosedOutcomeRecordsExactlyOneIndependentLane()
    {
        var bank = DatabasePhaseTestComposition.Create(true);
        var phase = DatabasePhaseKind.QuorumFollowerAwait;
        var outcomes = Enum.GetValues<DatabasePhaseOutcome>();

        foreach (var outcome in outcomes)
        {
            var started = bank.Begin();
            bank.End(phase, outcome, started);
        }

        var snapshot = bank.Capture();
        var phaseStart = (int)phase * OutcomeCount * BucketCount;
        for (var outcomeIndex = 0; outcomeIndex < OutcomeCount; outcomeIndex++)
        {
            var outcomeStart = phaseStart + outcomeIndex * BucketCount;
            await Assert.That(LaneTotal(snapshot.Histogram, outcomeStart)).IsEqualTo(1L);
        }

        await Assert.That(Total(snapshot.Histogram)).IsEqualTo((long)OutcomeCount);
        await Assert.That(snapshot.Histogram.All(static value => value >= 0)).IsTrue();
        await Assert.That(snapshot.BusyAttempts.All(static value => value == 0)).IsTrue();
        await Assert.That(snapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
    }

    [Test]
    public async Task PreviouslyReturnedSnapshotStaysDetachedAfterLaterUpdatesAndDegradation()
    {
        var bank = DatabasePhaseTestComposition.Create(true);
        var phase = DatabasePhaseKind.NativeTreeMutation;
        var firstStart = bank.Begin();
        bank.End(phase, DatabasePhaseOutcome.Completed, firstStart);
        bank.RecordBusy(phase);
        var earlier = bank.Capture();
        var laneStart = ((int)phase * OutcomeCount + (int)DatabasePhaseOutcome.Completed) * BucketCount;

        var laterStart = bank.Begin();
        bank.End(phase, DatabasePhaseOutcome.Completed, laterStart);
        bank.RecordBusy(phase);
        bank.End((DatabasePhaseKind)PhaseCount, DatabasePhaseOutcome.Completed, bank.Begin());
        var later = bank.Capture();

        await Assert.That(LaneTotal(earlier.Histogram, laneStart)).IsEqualTo(1L);
        await Assert.That(earlier.BusyAttempts[(int)phase]).IsEqualTo(1L);
        await Assert.That(earlier.Quality).IsEqualTo(DatabaseProfileQuality.None);
        await Assert.That(LaneTotal(later.Histogram, laneStart)).IsEqualTo(2L);
        await Assert.That(later.BusyAttempts[(int)phase]).IsEqualTo(2L);
        await Assert.That(later.Quality).IsEqualTo(DatabaseProfileQuality.InvalidDimension);
    }

    [Test]
    public async Task IndependentBanksKeepTheirCountersSeparate()
    {
        var first = DatabasePhaseTestComposition.Create(true);
        var second = DatabasePhaseTestComposition.Create(true);
        first.RecordBusy(DatabasePhaseKind.HeartbeatRoundsHold);
        var firstSnapshot = first.Capture();
        var secondSnapshot = second.Capture();

        await Assert.That(firstSnapshot.Histogram.All(static value => value == 0)).IsTrue();
        await Assert.That(Total(firstSnapshot.BusyAttempts)).IsEqualTo(1L);
        await Assert.That(secondSnapshot.Histogram.All(static value => value == 0)).IsTrue();
        await Assert.That(secondSnapshot.BusyAttempts.All(static value => value == 0)).IsTrue();
        await Assert.That(firstSnapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
        await Assert.That(secondSnapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
    }

    [Test]
    public async Task InvalidDimensionsAndElapsedTimeDegradeWithoutThrowing()
    {
        var bank = DatabasePhaseTestComposition.Create(true);
        var started = bank.Begin();
        bank.End((DatabasePhaseKind)InvalidPhase, DatabasePhaseOutcome.Completed, started);
        bank.End((DatabasePhaseKind)PhaseCount, DatabasePhaseOutcome.Completed, started);
        bank.End(DatabasePhaseKind.ReadRoundsWait, (DatabasePhaseOutcome)InvalidOutcome, started);
        bank.End(DatabasePhaseKind.ReadRoundsWait, (DatabasePhaseOutcome)OutcomeCount, started);
        bank.End(DatabasePhaseKind.ReadRoundsWait, DatabasePhaseOutcome.Completed, InvalidElapsed);
        bank.End(DatabasePhaseKind.ReadRoundsWait, DatabasePhaseOutcome.Completed, FutureTimestamp);
        bank.RecordBusy((DatabasePhaseKind)InvalidPhase);
        bank.RecordBusy((DatabasePhaseKind)PhaseCount);
        var snapshot = bank.Capture();
        var expected = DatabaseProfileQuality.InvalidDimension | DatabaseProfileQuality.InvalidElapsed;

        await Assert.That(snapshot.Quality).IsEqualTo(expected);
        await Assert.That(Total(snapshot.Histogram)).IsEqualTo(0L);
        await Assert.That(snapshot.BusyAttempts.All(static value => value == 0)).IsTrue();
    }

    private static long LaneTotal(ImmutableArray<long> values, int start)
    {
        long total = 0;
        for (var bucket = 0; bucket < BucketCount; bucket++)
        {
            total += values[start + bucket];
        }

        return total;
    }

    private static long Total(ImmutableArray<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }
}
