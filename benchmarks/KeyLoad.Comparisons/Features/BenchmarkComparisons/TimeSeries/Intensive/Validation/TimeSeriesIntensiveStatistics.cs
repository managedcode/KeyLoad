namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveStatistics
{
    internal static SampleAggregate Fold(IEnumerable<SampleRecord> expected)
    {
        const long FirstElementIndexLong = 0L;
        const long ZeroAccumulatorLong = 0L;
        const int NoObservedItems = 0;

        ArgumentNullException.ThrowIfNull(expected);
        var count = FirstElementIndexLong;
        var sum = ZeroAccumulatorLong;
        var minimum = long.MaxValue;
        var maximum = long.MinValue;
        foreach (var row in expected)
        {
            var quarters = checked((long)(row.Sample.Value * TimeSeriesIntensiveProfile.QuarterScale));
            if (quarters / (double)TimeSeriesIntensiveProfile.QuarterScale != row.Sample.Value)
            {
                throw new ArgumentException(TimeSeriesIntensiveErrors.QuarterValues, nameof(expected));
            }

            count++;
            sum = checked(sum + quarters);
            minimum = Math.Min(minimum, quarters);
            maximum = Math.Max(maximum, quarters);
        }

        return count == NoObservedItems ? new(NoObservedItems, NoObservedItems, null, null, null)
            : new(count, sum / (double)TimeSeriesIntensiveProfile.QuarterScale, minimum / (double)TimeSeriesIntensiveProfile.QuarterScale, maximum / (double)TimeSeriesIntensiveProfile.QuarterScale, sum / ((double)TimeSeriesIntensiveProfile.QuarterScale * count));
    }

    internal static void Validate(SampleAggregate expected, SampleAggregate actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null || !double.IsFinite(actual.Sum) || !Finite(actual.Minimum)
            || !Finite(actual.Maximum) || !Finite(actual.Average))
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.NonfiniteStatistics);
        }

        if (actual.Count != expected.Count || actual.Sum != expected.Sum || actual.Minimum != expected.Minimum
            || actual.Maximum != expected.Maximum || !AverageMatches(expected.Average, actual.Average))
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.Statistics);
        }
    }

    private static bool Finite(double? value) => !value.HasValue || double.IsFinite(value.Value);

    private static bool AverageMatches(double? expected, double? actual) => expected.HasValue
        ? actual.HasValue && Math.Abs(expected.Value - actual.Value) <= TimeSeriesIntensiveProfile.AverageTolerance
        : !actual.HasValue;
}
