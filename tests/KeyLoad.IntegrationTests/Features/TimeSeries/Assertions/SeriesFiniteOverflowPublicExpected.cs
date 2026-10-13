namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesFiniteOverflowPublicExpected
{
    internal const string OverflowDetail = "The time-series aggregate is not finite.";
    internal const string McpOverflowDetail = "The database operation could not be completed.";
    internal const string Series = "finite-overflow-public";
    internal const string Tags = "{\"visible\":17}";
    internal static DateTimeOffset FirstAt => TimeSeriesRf3Scenario.Start;
    internal static DateTimeOffset SecondAt => FirstAt.AddTicks(1);
    internal static DateTimeOffset End => FirstAt.AddTicks(2);
    internal static SampleData First() => new("finite-first", FirstAt, double.MaxValue);
    internal static SampleData Second() => new("finite-second", SecondAt, double.MaxValue);
    internal static SampleAggregate Finite() => new(1L, double.MaxValue,
        double.MaxValue, double.MaxValue, double.MaxValue);
    internal static SampleRecord[] Rows(bool purged) => purged
        ? [new(Series, Second(), 2L, Tags)]
        : [new(Series, First(), 1L, Tags), new(Series, Second(), 2L, Tags)];
    internal static LatestSampleResult Latest() => new(new(Series, Second(), 2L, Tags));
    internal static SampleRetentionStatus Retention(bool purged)
        => new(purged ? SecondAt : null, purged ? 1L : 0L, false);
    internal static SampleAggregateWindowsResult DenseWindows(bool purged)
        => new([new(FirstAt, SecondAt, purged ? new(0L, 0D, null, null, null) : Finite()),
            new(SecondAt, End, Finite())]);
    internal static SampleAggregateWindowsResult RepairedWideWindow()
        => new([new(FirstAt, End, Finite())]);
    internal static AggregateSampleWindowsRequest Windows(SeriesFiniteOverflowPublicState state, bool wide)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, Series, FirstAt, End,
            TimeSpan.FromTicks(wide ? 2 : 1), 2, 2);
    internal static ReadSamplesRequest Range(SeriesFiniteOverflowPublicState state)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, Series, FirstAt, SecondAt, 2);
    internal static ReadLatestSampleRequest LatestRequest(SeriesFiniteOverflowPublicState state)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, Series);
    internal static ReadSampleRetentionRequest RetentionRequest(SeriesFiniteOverflowPublicState state)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, Series);
    internal static AggregateSamplesRequest Whole(SeriesFiniteOverflowPublicState state)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, Series, FirstAt, End, 2);
    internal static AggregateSamplesRequest Narrow(SeriesFiniteOverflowPublicState state)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, Series, SecondAt, End, 1);
}
