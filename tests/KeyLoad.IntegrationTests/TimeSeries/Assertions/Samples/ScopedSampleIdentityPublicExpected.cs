namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class ScopedSampleIdentityPublicExpected
{
    internal const string A = "id-scope-a";
    internal const string B = "id-scope-b";
    internal const string SharedId = "shared-sample-id";
    internal const string Tags = "{\"visible\":17}";
    internal const string ChangedTags = "{\"visible\":18}";
    internal const string ConflictDetail = "A sample ID was reused with different content.";
    internal const string McpConflictDetail = "The database operation could not be completed.";
    internal static DateTimeOffset At => TimeSeriesClusterScenario.Start;
    internal static SampleData Sample(bool second) => new(SharedId, At, second ? 5D : 3D);
    internal static SampleRecord Record(bool second)
        => new(second ? B : A, Sample(second), 1L, Tags);
    internal static ReadSamplesRequest Range(ScopedSampleIdentityPublicState state, string series)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, series, At, At.AddTicks(2), 4);
    internal static AggregateSamplesRequest Aggregate(ScopedSampleIdentityPublicState state, string series)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, series, At, At.AddTicks(2), 4);
    internal static SampleAggregate AggregateValue(bool second)
        => second ? new(1L, 5D, 5D, 5D, 5D) : new(1L, 3D, 3D, 3D, 3D);
    internal static CommandRequest Original(TimeSeriesClusterScenario scenario)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, A, [Sample(false)], Tags),
             new AppendSamples(TimeSeriesClusterScenario.Set, B, [Sample(true)], Tags)]);
    internal static CommandRequest ValueConflict(TimeSeriesClusterScenario scenario)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, B,
                [new("transient-sibling", At.AddTicks(1), 7D)], Tags),
             new AppendSamples(TimeSeriesClusterScenario.Set, A, [new(SharedId, At, 13D)], Tags)]);
    internal static CommandRequest TagConflict(TimeSeriesClusterScenario scenario)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, A, [Sample(false)], ChangedTags)]);
}
