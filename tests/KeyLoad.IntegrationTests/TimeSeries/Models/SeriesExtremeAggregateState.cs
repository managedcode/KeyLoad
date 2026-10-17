using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SeriesExtremeAggregateState(TimeSeriesRf3Scenario Scenario,
    ImmutableArray<SampleData> Initial, ImmutableArray<SampleData> Late, ImmutableArray<SampleData> Extrema,
    AtomicPartitionPlacementResolution Placement)
{
    internal McpPersistedIdentity? Reader
    {
        get;
        set;
    }
    internal OutboxStatus? Outbox
    {
        get;
        set;
    }
    internal bool HasExtrema
    {
        get;
        set;
    }
    internal bool Sealed
    {
        get;
        set;
    }
    internal Guid WindowId
    {
        get;
    } = Guid.NewGuid();
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Receipts
    {
        get;
    } = [];
}
