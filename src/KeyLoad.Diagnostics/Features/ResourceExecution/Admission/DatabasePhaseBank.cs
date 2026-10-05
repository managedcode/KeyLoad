using System.Collections.Immutable;
using System.Diagnostics;

namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>A fixed-size, callback-free, cumulative phase counter bank.</summary>
public sealed class DatabasePhaseBank
{
    private const int PhaseCount = 32;
    private const int OutcomeCount = 6;
    private const int BucketCount = 16;
    private const int StripeCount = 4;
    private const int HistogramLength = PhaseCount * OutcomeCount * BucketCount;
    private const long DisabledTimestamp = -1;

    private readonly bool _enabled;
    private readonly long[][] _histogramStripes;
    private readonly long[][] _busyStripes;
    private readonly long[] _boundaries;
    private int _quality;

    /// <summary>Creates a bank with a fixed enabled mode for its entire lifetime.</summary>
    public DatabasePhaseBank(bool enabled)
    {
        _enabled = enabled;
        _histogramStripes = enabled ? new long[StripeCount][] : Array.Empty<long[]>();
        _busyStripes = enabled ? new long[StripeCount][] : Array.Empty<long[]>();
        if (enabled)
        {
            for (var stripe = DatabasePhaseValues.FirstStripeIndex; stripe < StripeCount; stripe++)
            {
                _histogramStripes[stripe] = new long[HistogramLength];
                _busyStripes[stripe] = new long[PhaseCount];
            }
        }

        _boundaries = enabled
            ? DatabasePhaseArithmetic.CreateBoundaries(Stopwatch.Frequency)
            : Array.Empty<long>();
    }

    /// <summary>Gets the immutable mode selected when this bank was created.</summary>
    public bool IsEnabled => _enabled;

    /// <summary>Returns an actual monotonic timestamp, or the disabled sentinel.</summary>
    public long Begin() => _enabled ? Stopwatch.GetTimestamp() : DisabledTimestamp;

    /// <summary>Records one completed scope without throwing into its producer.</summary>
    public void End(DatabasePhaseKind phase, DatabasePhaseOutcome outcome, long started)
    {
        if (!_enabled)
        {
            return;
        }

        var phaseIndex = (int)phase;
        var outcomeIndex = (int)outcome;
        if ((uint)phaseIndex >= PhaseCount || (uint)outcomeIndex >= OutcomeCount)
        {
            MarkQuality(DatabaseProfileQuality.InvalidDimension);
            return;
        }

        var finished = Stopwatch.GetTimestamp();
        if (started < DatabasePhaseValues.MinimumStartTimestamp || started > finished)
        {
            MarkQuality(DatabaseProfileQuality.InvalidElapsed);
            return;
        }

        var elapsed = finished - started;
        var bucket = DatabasePhaseArithmetic.BucketFor(elapsed, _boundaries);
        if ((uint)bucket >= BucketCount)
        {
            MarkQuality(DatabaseProfileQuality.InvalidElapsed);
            return;
        }

        var stripe = CurrentStripe();
        var lane = (phaseIndex * OutcomeCount + outcomeIndex) * BucketCount + bucket;
        MarkQuality(DatabasePhaseArithmetic.TryIncrement(ref _histogramStripes[stripe][lane]));
    }

    /// <summary>Records one bounded admission rejection in its separate phase lane.</summary>
    public void RecordBusy(DatabasePhaseKind phase)
    {
        if (!_enabled)
        {
            return;
        }

        var phaseIndex = (int)phase;
        if ((uint)phaseIndex >= PhaseCount)
        {
            MarkQuality(DatabaseProfileQuality.InvalidDimension);
            return;
        }

        var stripe = CurrentStripe();
        MarkQuality(DatabasePhaseArithmetic.TryIncrement(ref _busyStripes[stripe][phaseIndex]));
    }

    /// <summary>Returns a detached cumulative observation without resetting counters.</summary>
    public DatabasePhaseSnapshot Capture()
    {
        if (!_enabled)
        {
            return new DatabasePhaseSnapshot(
                false,
                DatabasePhaseValues.DisabledSnapshotFrequency,
                DatabasePhaseValues.DisabledSnapshotMonotonicTimestamp,
                DatabasePhaseValues.DisabledSnapshotMonotonicTimestamp,
                DatabaseProfileQuality.None,
                ImmutableArray<long>.Empty,
                ImmutableArray<long>.Empty);
        }

        var started = Stopwatch.GetTimestamp();
        var histogram = CaptureHistogram();
        var busyAttempts = CaptureBusyAttempts();
        var quality = (DatabaseProfileQuality)Volatile.Read(ref _quality);
        var finished = Stopwatch.GetTimestamp();
        return new DatabasePhaseSnapshot(
            true,
            Stopwatch.Frequency,
            started,
            finished,
            quality,
            histogram,
            busyAttempts);
    }

    private ImmutableArray<long> CaptureHistogram()
    {
        var values = ImmutableArray.CreateBuilder<long>(HistogramLength);
        for (var lane = DatabasePhaseValues.FirstHistogramLane; lane < HistogramLength; lane++)
        {
            var total = DatabasePhaseValues.EmptyCounterTotal;
            for (var stripe = DatabasePhaseValues.FirstStripeIndex; stripe < StripeCount; stripe++)
            {
                total = DatabasePhaseArithmetic.AddSaturating(
                    total,
                    Interlocked.Read(ref _histogramStripes[stripe][lane]),
                    out var overflow);
                if (overflow)
                {
                    MarkQuality(DatabaseProfileQuality.SnapshotOverflow);
                }
            }

            values.Add(total);
        }

        return values.MoveToImmutable();
    }

    private ImmutableArray<long> CaptureBusyAttempts()
    {
        var values = ImmutableArray.CreateBuilder<long>(PhaseCount);
        for (var phase = DatabasePhaseValues.FirstPhaseIndex; phase < PhaseCount; phase++)
        {
            var total = DatabasePhaseValues.EmptyCounterTotal;
            for (var stripe = DatabasePhaseValues.FirstStripeIndex; stripe < StripeCount; stripe++)
            {
                total = DatabasePhaseArithmetic.AddSaturating(
                    total,
                    Interlocked.Read(ref _busyStripes[stripe][phase]),
                    out var overflow);
                if (overflow)
                {
                    MarkQuality(DatabaseProfileQuality.SnapshotOverflow);
                }
            }

            values.Add(total);
        }

        return values.MoveToImmutable();
    }

    private static int CurrentStripe() => Environment.CurrentManagedThreadId & (StripeCount - DatabasePhaseValues.CounterIncrement);

    private void MarkQuality(DatabaseProfileQuality quality)
    {
        if (quality != DatabaseProfileQuality.None)
        {
            _ = Interlocked.Or(ref _quality, (int)quality);
        }
    }
}
