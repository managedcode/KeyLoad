namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveSummary
{
    private const int SingleItemCount = 1;

    internal static TimeSeriesIntensiveMeasurement Calculate(ReadOnlySpan<TimeSeriesIntensiveAttempt> attempts, long wallTicks, long frequency)
    {
        const int NoObservedItems = 0;
        const long NoObservedItemsLong = 0L;
        const int FirstElementIndex = 0;
        const int SingleItemCount = 1;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(wallTicks, NoObservedItems);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frequency, NoObservedItems);
        if (attempts.Length != TimeSeriesIntensiveProfile.OperationCount)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidSummary);
        }

        var latencies = new long[attempts.Length];
        var successes = NoObservedItems;
        var validationTicks = NoObservedItemsLong;
        for (var index = FirstElementIndex; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Index != index || attempt.Repetition != attempts[FirstElementIndex].Repetition
                || attempt.Worker != index % TimeSeriesIntensiveProfile.Concurrency
                || !Enum.IsDefined(attempt.Outcome) || attempt.Outcome == TimeSeriesIntensiveOutcome.NotStarted
                || attempt.LatencyTicks < NoObservedItems || attempt.ValidationTicks < NoObservedItems)
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidSummary);
            }

            latencies[index] = attempt.LatencyTicks;
            validationTicks = checked(validationTicks + attempt.ValidationTicks);
            successes += attempt.Outcome == TimeSeriesIntensiveOutcome.Succeeded ? SingleItemCount : NoObservedItems;
        }

        Array.Sort(latencies);
        var seconds = wallTicks / (double)frequency;
        var scale = TimeSeriesIntensiveRuntimePolicy.MillisecondsPerSecond / frequency;
        return new(attempts.Length, successes, seconds, successes / seconds, validationTicks / (double)frequency,
            Rank(latencies, TimeSeriesIntensiveRuntimePolicy.MedianPercentile) * scale,
            Rank(latencies, TimeSeriesIntensiveRuntimePolicy.P95Percentile) * scale,
            Rank(latencies, TimeSeriesIntensiveRuntimePolicy.P99Percentile) * scale);
    }

    internal static long Percentile(ReadOnlySpan<long> values, double percentile)
    {
        const int NoObservedItems = 0;
        const int SingleItemCount = 1;
        const int ZeroAccumulator = 0;

        if (values.IsEmpty || !double.IsFinite(percentile) || percentile is <= NoObservedItems or > SingleItemCount)
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidPercentile, nameof(values));
        }

        var sorted = values.ToArray();
        if (sorted.Any(value => value < ZeroAccumulator))
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidPercentile, nameof(values));
        }

        Array.Sort(sorted);
        return Rank(sorted, percentile);
    }

    internal static double MedianFive(ReadOnlySpan<double> values)
    {
        const int NoObservedItems = 0;

        if (values.Length != TimeSeriesIntensiveProfile.RepetitionCount)
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidMedian, nameof(values));
        }

        Span<double> sorted = stackalloc double[TimeSeriesIntensiveProfile.RepetitionCount];
        values.CopyTo(sorted);
        foreach (var value in sorted)
        {
            if (!double.IsFinite(value) || value < NoObservedItems)
            {
                throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidMedian, nameof(values));
            }
        }

        sorted.Sort();
        return sorted[TimeSeriesIntensiveProfile.RepetitionCount / TimeSeriesIntensiveRuntimePolicy.MiddleDivisor];
    }

    private static long Rank(ReadOnlySpan<long> sorted, double percentile) => sorted[(int)Math.Ceiling(sorted.Length * percentile) - SingleItemCount];
}
