namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Integer-only helpers shared by the bounded phase bank and its tests.</summary>
internal static class DatabasePhaseArithmetic
{
    private const int BoundaryCount = 15;
    private const int CasAttempts = 4;
    private const int InvalidBucket = -1;
    private const uint MicrosecondsPerSecond = 1_000_000;
    private static readonly int[] BoundaryMicroseconds =
    [DatabasePhaseValues.FiftyMicroseconds, DatabasePhaseValues.OneHundredMicroseconds,
        DatabasePhaseValues.TwoHundredFiftyMicroseconds, DatabasePhaseValues.FiveHundredMicroseconds,
        DatabasePhaseValues.OneThousandMicroseconds, DatabasePhaseValues.TwoThousandMicroseconds,
        DatabasePhaseValues.FiveThousandMicroseconds, DatabasePhaseValues.TenThousandMicroseconds,
        DatabasePhaseValues.TwentyThousandMicroseconds, DatabasePhaseValues.FiftyThousandMicroseconds,
        DatabasePhaseValues.OneHundredThousandMicroseconds,
        DatabasePhaseValues.TwoHundredFiftyThousandMicroseconds,
        DatabasePhaseValues.FiveHundredThousandMicroseconds,
        DatabasePhaseValues.OneMillionMicroseconds, DatabasePhaseValues.FiveMillionMicroseconds];

    /// <summary>Precomputes inclusive threshold ticks with wide startup arithmetic.</summary>
    internal static long[] CreateBoundaries(long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        var boundaries = new long[BoundaryCount];
        for (var index = DatabasePhaseValues.FirstBoundaryIndex; index < boundaries.Length; index++)
        {
            var numerator = (UInt128)(ulong)frequency * (uint)BoundaryMicroseconds[index];
            var ticks = numerator / MicrosecondsPerSecond;
            boundaries[index] = ticks > (UInt128)long.MaxValue ? long.MaxValue : (long)ticks;
        }

        return boundaries;
    }

    /// <summary>Maps elapsed Stopwatch ticks to one of sixteen inclusive buckets.</summary>
    internal static int BucketFor(long elapsed, long frequency)
    {
        if (elapsed < DatabasePhaseValues.MinimumElapsedTicks || frequency <= DatabasePhaseValues.MinimumStopwatchFrequency)
        {
            return InvalidBucket;
        }

        return BucketFor(elapsed, CreateBoundaries(frequency));
    }

    /// <summary>Uses precomputed inclusive thresholds without hot-path arithmetic.</summary>
    internal static int BucketFor(long elapsed, ReadOnlySpan<long> boundaries)
    {
        if (elapsed < DatabasePhaseValues.MinimumElapsedTicks || boundaries.Length != BoundaryCount)
        {
            return InvalidBucket;
        }

        for (var index = DatabasePhaseValues.FirstBoundaryIndex; index < boundaries.Length; index++)
        {
            if (elapsed <= boundaries[index])
            {
                return index;
            }
        }

        return BoundaryCount;
    }

    /// <summary>Attempts at most four compare-exchanges and never wraps a counter.</summary>
    internal static DatabaseProfileQuality TryIncrement(ref long counter)
    {
        for (var attempt = DatabasePhaseValues.FirstCasAttempt; attempt < CasAttempts; attempt++)
        {
            var observed = Interlocked.Read(ref counter);
            if (observed == long.MaxValue || observed < DatabasePhaseValues.MinimumCounterValue)
            {
                return DatabaseProfileQuality.SaturatedCounter;
            }

            var incremented = observed + DatabasePhaseValues.CounterIncrement;
            if (Interlocked.CompareExchange(ref counter, incremented, observed) == observed)
            {
                return incremented == long.MaxValue
                    ? DatabaseProfileQuality.SaturatedCounter
                    : DatabaseProfileQuality.None;
            }
        }

        return DatabaseProfileQuality.ContentionDropped;
    }

    /// <summary>Sums nonnegative snapshot counters exactly or saturates with a marker.</summary>
    internal static long AddSaturating(long left, long right, out bool overflow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        overflow = left > long.MaxValue - right;
        return overflow ? long.MaxValue : left + right;
    }
}
