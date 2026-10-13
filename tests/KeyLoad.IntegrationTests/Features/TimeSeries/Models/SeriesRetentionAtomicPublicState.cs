using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class SeriesRetentionAtomicPublicState(TimeSeriesRf3Scenario scenario,
    McpPersistedIdentity reader, AtomicPartitionPlacementResolution placement, OutboxStatus outbox)
{
    internal TimeSeriesRf3Scenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal OutboxStatus CurrentOutbox { get; set; } = outbox;
    internal required CommandRequest Seed { get; init; }
    internal required CommandRequest Expiry { get; init; }
    internal required CommandRequest EchoSdk { get; init; }
    internal required CommandRequest EchoMcp { get; init; }
    internal required CommandRequest ConflictSdk { get; init; }
    internal required CommandRequest ConflictMcp { get; init; }
    internal required CommandRequest MixedSdk { get; init; }
    internal required CommandRequest MixedMcp { get; init; }
    internal required CommandRequest Fresh { get; init; }
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal bool Purged { get; set; }
    internal bool Advanced { get; set; }
}
