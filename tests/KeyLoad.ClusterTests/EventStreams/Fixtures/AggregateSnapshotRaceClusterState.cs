using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.EventStreams;

internal sealed class AggregateSnapshotRaceClusterState(AggregateReplayClusterScenario scenario,
    CommitReceipt seed)
{
    internal AggregateReplayClusterScenario Scenario { get; } = scenario;
    internal CommitReceipt Seed { get; } = seed;
    internal CommandRequest SeedCommand { get; set; } = null!;
    internal ImmutableArray<EventRecord> Events { get; set; } = [];
    internal CommandRequest[] Commands
    {
        get;
    } = [Snapshot(scenario, 1, "{ \"count\" : 1 }", 0),
        Snapshot(scenario, 2, "{\"count\":3}", 0)];
    internal CommandRequest? Winner
    {
        get; set;
    }
    internal CommitReceipt? WinnerReceipt
    {
        get; set;
    }
    internal CommandRequest? Loser
    {
        get; set;
    }
    internal CommandRequest? Healthy
    {
        get; set;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get; set;
    }
    internal string OriginalImage { get; set; } = string.Empty;
    internal bool Updated
    {
        get; set;
    }
    internal static CommandRequest Snapshot(AggregateReplayClusterScenario owner, long source, string state, long version)
        => new(Guid.NewGuid(), owner.Partition, [new StoreAggregateSnapshot(AggregateReplayClusterTokens.StreamSet,
            AggregateReplayClusterTokens.StreamId, source, AggregateReplayClusterTokens.Reducer, 1, state, version)]);
}
