using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class AggregateSnapshotRaceRf3State(AggregateReplayRf3Scenario scenario,
    AtomicPartitionPlacementResolution placement, CommitReceipt seed)
{
    internal AggregateReplayRf3Scenario Scenario { get; } = scenario;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
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
    internal static CommandRequest Snapshot(AggregateReplayRf3Scenario owner, long source, string state, long version)
        => new(Guid.NewGuid(), owner.Partition, [new StoreAggregateSnapshot(AggregateReplayRf3Tokens.StreamSet,
            AggregateReplayRf3Tokens.StreamId, source, AggregateReplayRf3Tokens.Reducer, 1, state, version)]);
}
