using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class SeriesUtcTiePublicState(TimeSeriesRf3Scenario scenario, ImmutableArray<SampleData> original,
    McpPersistedIdentity reader, AtomicPartitionPlacementResolution placement)
{
    internal TimeSeriesRf3Scenario Scenario { get; } = scenario;
    internal ImmutableArray<SampleData> Original { get; } = original;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
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
