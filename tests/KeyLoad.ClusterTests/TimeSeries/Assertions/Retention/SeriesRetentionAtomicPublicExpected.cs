namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SeriesRetentionAtomicPublicExpected
{
    internal const string Series = "public-retention-atomic";
    internal const string Tags = "{\"visible\":17}";
    internal const string AppendKind = "appendSamples";
    internal const string ExpireKind = "expireSamples";
    internal const string KnownId = "known-purged";
    internal const string FloorId = "floor-visible";
    internal const string FreshId = "fresh-after-refusal";
    internal const string ExpiredSdkId = "new-expired-sdk";
    internal const string ExpiredMcpId = "new-expired-mcp";
    internal const string ConflictDetail = "A sample ID was reused with different content.";
    internal const string FloorDetail = "A sample cannot be appended before the series retention floor.";
    internal const string McpDetail = "The database operation could not be completed.";
    internal const int SampleLimit = 3;
    internal const int MaximumDeletes = 2;
    internal const int WindowLimit = 3;
    internal static DateTimeOffset At => TimeSeriesClusterScenario.Start;
    internal static DateTimeOffset FloorAt => At.AddTicks(1);
    internal static DateTimeOffset FreshAt => At.AddTicks(2);
    internal static DateTimeOffset End => At.AddTicks(3);
    internal static SampleData Known() => new(KnownId, At, 3D);
    internal static SampleData Floor() => new(FloorId, FloorAt, 5D);
    internal static SampleData Fresh() => new(FreshId, FreshAt, 7D);
    internal static SampleData ChangedKnown() => new(KnownId, At, 13D);
    internal static SampleData NewExpired(bool mcp) => new(mcp ? ExpiredMcpId : ExpiredSdkId, At, 11D);

    internal static SampleRecord[] Rows(SeriesRetentionAtomicPublicState state)
    {
        if (state.Advanced)
        {
            return [new(Series, Floor(), 2L, Tags), new(Series, Fresh(), 3L, Tags)];
        }
        if (state.Purged)
        {
            return [new(Series, Floor(), 2L, Tags)];
        }
        return [new(Series, Known(), 1L, Tags), new(Series, Floor(), 2L, Tags)];
    }

    internal static LatestSampleResult Latest(SeriesRetentionAtomicPublicState state)
        => new(state.Advanced ? new(Series, Fresh(), 3L, Tags) : new(Series, Floor(), 2L, Tags));
    internal static SampleRetentionStatus Retention(SeriesRetentionAtomicPublicState state)
        => new(state.Purged ? FloorAt : null, state.Purged ? 1L : 0L, false);
    internal static SampleAggregate Aggregate(SeriesRetentionAtomicPublicState state)
    {
        if (state.Advanced)
        {
            return new(2L, 12D, 5D, 7D, 6D);
        }
        if (state.Purged)
        {
            return new(1L, 5D, 5D, 5D, 5D);
        }
        return new(2L, 8D, 3D, 5D, 4D);
    }

    internal static SampleAggregateWindowsResult Windows(SeriesRetentionAtomicPublicState state)
        => new([new(At, FloorAt, state.Purged ? new(0L, 0D, null, null, null) : new(1L, 3D, 3D, 3D, 3D)),
            new(FloorAt, FreshAt, new(1L, 5D, 5D, 5D, 5D)),
            new(FreshAt, End, state.Advanced ? new(1L, 7D, 7D, 7D, 7D) : new(0L, 0D, null, null, null))]);
    internal static ReadSamplesRequest Range(SeriesRetentionAtomicPublicState state)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, Series, At, End, SampleLimit);
    internal static ReadLatestSampleRequest LatestRequest(SeriesRetentionAtomicPublicState state)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, Series);
    internal static ReadSampleRetentionRequest RetentionRequest(SeriesRetentionAtomicPublicState state)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, Series);
    internal static AggregateSamplesRequest AggregateRequest(SeriesRetentionAtomicPublicState state)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, Series, At, End, SampleLimit);
    internal static AggregateSampleWindowsRequest WindowsRequest(SeriesRetentionAtomicPublicState state)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, Series, At, End,
            TimeSeriesClusterScenario.OneTick, SampleLimit, WindowLimit);
}
