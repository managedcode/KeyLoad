namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveSummary
{
    internal static TimeSeriesIntensiveMeasurement Calculate(ReadOnlySpan<TimeSeriesIntensiveAttempt> attempts, long wallTicks, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(wallTicks, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frequency, 0);
        if (attempts.Length != TimeSeriesIntensiveProfile.OperationCount)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidSummary);
        }

        var latencies = new long[attempts.Length];
        var successes = 0;
        var validationTicks = 0L;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Index != index || attempt.Repetition != attempts[0].Repetition
                || attempt.Worker != index % TimeSeriesIntensiveProfile.Concurrency
                || !Enum.IsDefined(attempt.Outcome) || attempt.Outcome == TimeSeriesIntensiveOutcome.NotStarted
                || attempt.LatencyTicks < 0 || attempt.ValidationTicks < 0)
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveRuntimeErrors.InvalidSummary);
            }

            latencies[index] = attempt.LatencyTicks;
            validationTicks = checked(validationTicks + attempt.ValidationTicks);
            successes += attempt.Outcome == TimeSeriesIntensiveOutcome.Succeeded ? 1 : 0;
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
        if (values.IsEmpty || !double.IsFinite(percentile) || percentile is <= 0 or > 1)
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidPercentile, nameof(values));
        }

        var sorted = values.ToArray();
        if (sorted.Any(value => value < 0))
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidPercentile, nameof(values));
        }

        Array.Sort(sorted);
        return Rank(sorted, percentile);
    }

    internal static double MedianFive(ReadOnlySpan<double> values)
    {
        if (values.Length != TimeSeriesIntensiveProfile.RepetitionCount)
        {
            throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidMedian, nameof(values));
        }

        Span<double> sorted = stackalloc double[TimeSeriesIntensiveProfile.RepetitionCount];
        values.CopyTo(sorted);
        foreach (var value in sorted)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentException(TimeSeriesIntensiveRuntimeErrors.InvalidMedian, nameof(values));
            }
        }

        sorted.Sort();
        return sorted[TimeSeriesIntensiveProfile.RepetitionCount / TimeSeriesIntensiveRuntimePolicy.MiddleDivisor];
    }

    private static long Rank(ReadOnlySpan<long> sorted, double percentile) => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
}
