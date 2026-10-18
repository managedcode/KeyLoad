using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensiveExpectations(ImmutableArray<TimeSeriesIntensiveReadPlan> Ranges,
    ImmutableArray<ImmutableArray<SampleRecord>> Raw, ImmutableArray<SampleAggregate> Aggregates,
    ImmutableArray<ImmutableArray<SampleAggregateWindow>> Windows, ImmutableArray<DateTimeOffset> LatestCuts,
    ImmutableArray<SampleRecord?> Latest, ImmutableArray<SampleData> AppendSamples)
{
    internal static TimeSeriesIntensiveExpectations Create()
    {
        const int FirstElementIndex = 0;

        var ranges = ImmutableArray.CreateBuilder<TimeSeriesIntensiveReadPlan>(TimeSeriesIntensiveProfile.RangeGroups);
        var raw = ImmutableArray.CreateBuilder<ImmutableArray<SampleRecord>>(ranges.Capacity);
        var aggregates = ImmutableArray.CreateBuilder<SampleAggregate>(ranges.Capacity);
        var windows = ImmutableArray.CreateBuilder<ImmutableArray<SampleAggregateWindow>>(ranges.Capacity);
        for (var index = FirstElementIndex; index < ranges.Capacity; index++)
        {
            var range = TimeSeriesIntensivePlans.Read(index);
            ranges.Add(range);
            raw.Add(TimeSeriesIntensiveOracle.Raw(range));
            aggregates.Add(TimeSeriesIntensiveOracle.Aggregate(range));
            windows.Add(TimeSeriesIntensiveOracle.Windows(range));
        }

        var cuts = ImmutableArray.CreateBuilder<DateTimeOffset>(TimeSeriesIntensiveProfile.GroupCount);
        var latest = ImmutableArray.CreateBuilder<SampleRecord?>(cuts.Capacity);
        for (var index = FirstElementIndex; index < cuts.Capacity; index++)
        {
            var cut = TimeSeriesIntensivePlans.Read(index).LatestAtOrBefore;
            cuts.Add(cut);
            latest.Add(TimeSeriesIntensiveOracle.Latest(cut));
        }

        var samples = ImmutableArray.CreateBuilder<SampleData>(TimeSeriesIntensiveProfile.OperationCount);
        for (var index = FirstElementIndex; index < samples.Capacity; index++)
        {
            samples.Add(TimeSeriesIntensiveCorpus.AppendSample(index));
        }

        return new(ranges.MoveToImmutable(), raw.MoveToImmutable(), aggregates.MoveToImmutable(), windows.MoveToImmutable(),
            cuts.MoveToImmutable(), latest.MoveToImmutable(), samples.MoveToImmutable());
    }
}
