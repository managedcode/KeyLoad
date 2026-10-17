using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class ScopedSampleIdentityPublicState(TimeSeriesRf3Scenario scenario,
    McpPersistedIdentity reader, AtomicPartitionPlacementResolution placement)
{
    internal TimeSeriesRf3Scenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal OutboxStatus Outbox { get; set; } = null!;
    internal CommandRequest ValueConflict { get; } = ScopedSampleIdentityPublicExpected.ValueConflict(scenario);
    internal CommandRequest TagConflict { get; } = ScopedSampleIdentityPublicExpected.TagConflict(scenario);
}
