using System.Collections.Immutable;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseBankConcurrencyTests
{
    private const int UpdateCount = 24_000;
    private const int ConcurrentWorkCount = 24_000;
    private const int CaptureStride = 512;
    private const int ConcurrentCaptureCount = (ConcurrentWorkCount + CaptureStride - 1) / CaptureStride;
    private const int PhaseCount = 32;
    private const int OutcomeCount = 6;
    private const int BucketCount = 16;
    private const int HistogramLength = PhaseCount * OutcomeCount * BucketCount;
    private const int BusyLength = PhaseCount;
    private const int DeadlineMilliseconds = 30_000;
    private const long TotalAttempts = UpdateCount + ConcurrentWorkCount - ConcurrentCaptureCount;

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
    public async Task ConcurrentUpdatesRemainMonotonicAndExposeBoundedContentionLoss(int stripeCount, int maximumCasAttempts)
    {
        var bank = DatabasePhaseTestComposition.Create(true, stripeCount, maximumCasAttempts);
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(DeadlineMilliseconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var options = new ParallelOptions { CancellationToken = deadline.Token };
        var first = bank.Capture();
        var midpoint = UpdateCount / 2;
        RunUpdates(bank, options, 0, midpoint);
        var middle = bank.Capture();
        RunUpdates(bank, options, midpoint, UpdateCount);
        var settled = bank.Capture();
        var liveCaptures = CaptureWhileRecording(bank, options);
        var final = bank.Capture();
        var observed = Total(final.Histogram);
        var dropped = (final.Quality & DatabaseProfileQuality.ContentionDropped) != 0;

        await Assert.That(Total(middle.Histogram)).IsGreaterThan(0L);
        await Assert.That(observed).IsGreaterThan(0L);
        await Assert.That(IsMonotonic(first.Histogram, middle.Histogram)).IsTrue();
        await Assert.That(IsMonotonic(middle.Histogram, settled.Histogram)).IsTrue();
        await Assert.That(IsMonotonic(settled.Histogram, final.Histogram)).IsTrue();
        await Assert.That(IsQualityMonotonic(first.Quality, middle.Quality)).IsTrue();
        await Assert.That(IsQualityMonotonic(middle.Quality, settled.Quality)).IsTrue();
        await Assert.That(IsQualityMonotonic(settled.Quality, final.Quality)).IsTrue();
        await Assert.That(liveCaptures.Length).IsEqualTo(ConcurrentCaptureCount);
        await Assert.That(liveCaptures.All(IsValidConcurrentCapture)).IsTrue();
        await Assert.That(observed).IsLessThanOrEqualTo(TotalAttempts);
        await Assert.That(dropped || observed == TotalAttempts).IsTrue();
    }

    private static void RunUpdates(DatabasePhaseBank bank, ParallelOptions options, int start, int end) =>
        Parallel.For(start, end, options, index => RecordOne(bank, index));

    private static DatabasePhaseSnapshot[] CaptureWhileRecording(DatabasePhaseBank bank, ParallelOptions options)
    {
        var captures = new DatabasePhaseSnapshot[ConcurrentCaptureCount];
        Parallel.For(0, ConcurrentWorkCount, options, index =>
        {
            if (index % CaptureStride == 0)
            {
                captures[index / CaptureStride] = bank.Capture();
            }
            else
            {
                RecordOne(bank, index + UpdateCount);
            }
        });

        return captures;
    }

    private static bool IsValidConcurrentCapture(DatabasePhaseSnapshot snapshot) =>
        snapshot is not null && snapshot.Enabled && snapshot.Histogram.Length == HistogramLength &&
        snapshot.BusyAttempts.Length == BusyLength && Total(snapshot.Histogram) <= TotalAttempts;

    private static bool IsMonotonic(ImmutableArray<long> earlier, ImmutableArray<long> later)
    {
        if (earlier.Length != later.Length)
        {
            return false;
        }

        for (var index = 0; index < earlier.Length; index++)
        {
            if (earlier[index] > later[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsQualityMonotonic(DatabaseProfileQuality earlier, DatabaseProfileQuality later) =>
        (earlier & later) == earlier;

    private static long Total(ImmutableArray<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    private static void RecordOne(DatabasePhaseBank bank, int index)
    {
        var kind = (DatabasePhaseKind)(index % PhaseCount);
        var started = bank.Begin();
        bank.End(kind, DatabasePhaseOutcome.Completed, started);
    }
}
