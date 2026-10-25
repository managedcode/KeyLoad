using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed class SeriesRetentionAtomicPublicState(TimeSeriesClusterScenario scenario,
    McpPersistedIdentity reader, OutboxStatus outbox)
{
    internal TimeSeriesClusterScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal OutboxStatus CurrentOutbox { get; set; } = outbox;
    internal required CommandRequest Seed
    {
        get; init;
    }
    internal required CommandRequest Expiry
    {
        get; init;
    }
    internal required CommandRequest EchoSdk
    {
        get; init;
    }
    internal required CommandRequest EchoMcp
    {
        get; init;
    }
    internal required CommandRequest ConflictSdk
    {
        get; init;
    }
    internal required CommandRequest ConflictMcp
    {
        get; init;
    }
    internal required CommandRequest MixedSdk
    {
        get; init;
    }
    internal required CommandRequest MixedMcp
    {
        get; init;
    }
    internal required CommandRequest Fresh
    {
        get; init;
    }
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal bool Purged
    {
        get; set;
    }
    internal bool Advanced
    {
        get; set;
    }
}
