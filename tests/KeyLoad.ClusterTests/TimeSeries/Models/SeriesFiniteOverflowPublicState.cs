using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed class SeriesFiniteOverflowPublicState(TimeSeriesClusterScenario scenario,
    McpPersistedIdentity reader, CommandRequest seed, CommitReceipt seedReceipt, CommandRequest expiry, OutboxStatus outbox)
{
    internal TimeSeriesClusterScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
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
