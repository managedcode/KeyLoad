using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class SeriesFiniteOverflowPublicState(TimeSeriesRf3Scenario scenario,
    McpPersistedIdentity reader, AtomicPartitionPlacementResolution placement,
    CommandRequest seed, CommitReceipt seedReceipt, CommandRequest expiry, OutboxStatus outbox)
{
    internal TimeSeriesRf3Scenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal CommandRequest Seed { get; } = seed;
    internal CommitReceipt SeedReceipt { get; } = seedReceipt;
    internal CommandRequest Expiry { get; } = expiry;
    internal CommitReceipt? ExpiryReceipt
    {
        get; set;
    }
    internal OutboxStatus CurrentOutbox { get; set; } = outbox;
    internal bool Purged
    {
        get; set;
    }
}
