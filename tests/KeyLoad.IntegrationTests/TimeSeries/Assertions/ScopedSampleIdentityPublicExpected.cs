namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ScopedSampleIdentityPublicExpected
{
    internal const string A = "id-scope-a";
    internal const string B = "id-scope-b";
    internal const string SharedId = "shared-sample-id";
    internal const string Tags = "{\"visible\":17}";
    internal const string ChangedTags = "{\"visible\":18}";
    internal const string ConflictDetail = "A sample ID was reused with different content.";
    internal const string McpConflictDetail = "The database operation could not be completed.";
    internal static DateTimeOffset At => TimeSeriesRf3Scenario.Start;
    internal static SampleData Sample(bool second) => new(SharedId, At, second ? 5D : 3D);
    internal static SampleRecord Record(bool second)
        => new(second ? B : A, Sample(second), 1L, Tags);
    internal static ReadSamplesRequest Range(ScopedSampleIdentityPublicState state, string series)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, series, At, At.AddTicks(2), 4);
    internal static AggregateSamplesRequest Aggregate(ScopedSampleIdentityPublicState state, string series)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, series, At, At.AddTicks(2), 4);
    internal static SampleAggregate AggregateValue(bool second)
        => second ? new(1L, 5D, 5D, 5D, 5D) : new(1L, 3D, 3D, 3D, 3D);
    internal static CommandRequest Original(TimeSeriesRf3Scenario scenario)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, A, [Sample(false)], Tags),
             new AppendSamples(TimeSeriesRf3Scenario.Set, B, [Sample(true)], Tags)]);
    internal static CommandRequest ValueConflict(TimeSeriesRf3Scenario scenario)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, B,
                [new("transient-sibling", At.AddTicks(1), 7D)], Tags),
             new AppendSamples(TimeSeriesRf3Scenario.Set, A, [new(SharedId, At, 13D)], Tags)]);
    internal static CommandRequest TagConflict(TimeSeriesRf3Scenario scenario)
        => new(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, A, [Sample(false)], ChangedTags)]);
}
