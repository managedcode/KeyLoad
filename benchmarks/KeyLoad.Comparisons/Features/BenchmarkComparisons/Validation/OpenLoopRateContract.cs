using System.Collections.Immutable;
using System.Diagnostics;

namespace KeyLoad.Comparisons;

internal static class OpenLoopRateContract
{
    internal const int PlannedOperations = 100_000;
    internal const int SampleCapacity = 4_096;
    internal const int ProgressInterval = 1_024;
    internal const long NanosecondsPerSecond = 1_000_000_000;
    internal static readonly ImmutableArray<int> AcceptedRates = [250, 1_000, 4_000];

    internal static void Validate(int rate)
    {
        if (!AcceptedRates.Contains(rate))
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }
    }

    internal static long DueOffsetNanoseconds(int index, int rate)
    {
        if ((uint)index >= PlannedOperations)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        Validate(rate);
        return checked((long)index * NanosecondsPerSecond) / rate;
    }

    internal static long ToStopwatchTicks(long nanoseconds)
    {
        var seconds = nanoseconds / NanosecondsPerSecond;
        var remainder = nanoseconds % NanosecondsPerSecond;
        return checked(seconds * Stopwatch.Frequency
            + remainder * Stopwatch.Frequency / NanosecondsPerSecond);
    }
}
