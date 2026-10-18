using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveOracle
{
    internal static ImmutableArray<SampleRecord> Raw(TimeSeriesIntensiveReadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Inclusive(plan.From, plan.Until);
    }

    internal static ImmutableArray<SampleRecord> SeedReadback(TimeSeriesIntensiveReadback readback)
    {
        ArgumentNullException.ThrowIfNull(readback);
        var expected = Inclusive(readback.From, readback.Until);
        if (!string.Equals(readback.SeriesId, TimeSeriesIntensiveProfile.SeedSeries, StringComparison.Ordinal)
            || expected.Length != readback.ExpectedCount)
        {
            throw new ArgumentException(TimeSeriesIntensiveErrors.InvalidSeedReadback, nameof(readback));
        }

        return expected;
    }

    internal static SampleRecord? Latest(TimeSeriesIntensiveReadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Latest(plan.LatestAtOrBefore);
    }

    internal static SampleRecord? Latest(DateTimeOffset atOrBefore) => TimeSeriesIntensiveCorpus.SeedOrdered
        .LastOrDefault(row => row.Sample.Timestamp <= atOrBefore);

    internal static SampleAggregate Aggregate(TimeSeriesIntensiveReadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Aggregate(plan.From, plan.Until);
    }

    internal static SampleAggregate Aggregate(DateTimeOffset from, DateTimeOffset untilExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(untilExclusive, from);
        return TimeSeriesIntensiveStatistics.Fold(TimeSeriesIntensiveCorpus.SeedOrdered.Where(row =>
            row.Sample.Timestamp >= from && row.Sample.Timestamp < untilExclusive));
    }

    internal static ImmutableArray<SampleAggregateWindow> Windows(TimeSeriesIntensiveReadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Windows(plan.From, plan.Until, TimeSeriesIntensiveProfile.WindowWidth);
    }

    internal static ImmutableArray<SampleAggregateWindow> Windows(DateTimeOffset from, DateTimeOffset untilExclusive, TimeSpan width)
    {
        const int NoObservedItems = 0;
        const int SingleItemCount = 1;
        const long FirstElementIndexLong = 0L;

        ArgumentOutOfRangeException.ThrowIfLessThan(untilExclusive, from);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, TimeSpan.Zero);
        var duration = untilExclusive.UtcTicks - from.UtcTicks;
        var count = duration / width.Ticks + (duration % width.Ticks == NoObservedItems ? NoObservedItems : SingleItemCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TimeSeriesIntensiveProfile.MaxWindows);
        var builder = ImmutableArray.CreateBuilder<SampleAggregateWindow>((int)count);
        for (var index = FirstElementIndexLong; index < count; index++)
        {
            var start = from.AddTicks(index * width.Ticks);
            var remaining = untilExclusive.UtcTicks - start.UtcTicks;
            var end = start.AddTicks(Math.Min(width.Ticks, remaining));
            builder.Add(new(start, end, Aggregate(start, end)));
        }

        return builder.MoveToImmutable();
    }

    internal static void ValidateRaw(ImmutableArray<SampleRecord> expected, ImmutableArray<SampleRecord> actual)
    {
        const int FirstElementIndex = 0;

        if (expected.IsDefault || actual.IsDefault || actual.Length != expected.Length)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.SampleCardinality);
        }

        var tags = new TimeSeriesIntensiveTagScope();
        for (var index = FirstElementIndex; index < expected.Length; index++)
        {
            _ = TimeSeriesIntensiveRowVerifier.Validate(expected[index], actual[index], tags);
        }
    }

    internal static void ValidateLatest(SampleRecord? expected, SampleRecord? actual)
    {
        if (expected is null && actual is null)
        {
            return;
        }

        _ = TimeSeriesIntensiveRowVerifier.Validate(expected, actual, new());
    }

    internal static void ValidateAggregate(SampleAggregate expected, SampleAggregate actual) =>
        TimeSeriesIntensiveStatistics.Validate(expected, actual);

    internal static void ValidateWindows(ImmutableArray<SampleAggregateWindow> expected, ImmutableArray<SampleAggregateWindow> actual)
    {
        const int FirstElementIndex = 0;

        if (expected.IsDefault || actual.IsDefault || actual.Length != expected.Length)
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.WindowCardinality);
        }

        for (var index = FirstElementIndex; index < expected.Length; index++)
        {
            var row = actual[index];
            if (row is null || row.From != expected[index].From || row.UntilExclusive != expected[index].UntilExclusive)
            {
                throw new ComparisonFailureException(TimeSeriesIntensiveErrors.WindowBoundary);
            }

            ValidateAggregate(expected[index].Aggregate, row.Aggregate);
        }
    }

    internal static void ValidateFullCount(string seriesId, SampleAggregate actual)
    {
        if (actual is null || actual.Count != TimeSeriesIntensivePlans.WholeSeriesCount(seriesId))
        {
            throw new ComparisonFailureException(TimeSeriesIntensiveErrors.WholeSeriesCount);
        }
    }

    private static ImmutableArray<SampleRecord> Inclusive(DateTimeOffset from, DateTimeOffset until)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(until, from);
        return TimeSeriesIntensiveCorpus.SeedOrdered.Where(row => row.Sample.Timestamp >= from
            && row.Sample.Timestamp <= until).ToImmutableArray();
    }
}
