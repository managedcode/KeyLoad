using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed class SeriesUtcTiePublicState(TimeSeriesClusterScenario scenario, ImmutableArray<SampleData> original,
    McpPersistedIdentity reader)
{
    internal TimeSeriesClusterScenario Scenario { get; } = scenario;
    internal ImmutableArray<SampleData> Original { get; } = original;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Receipts { get; } = [];
    internal bool Purged
    {
        get; set;
    }
    internal bool Fresh
    {
        get; set;
    }
    internal OutboxStatus Outbox { get; set; } = null!;
}
