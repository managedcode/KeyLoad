using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal sealed record SeriesExtremeAggregateState(TimeSeriesClusterScenario Scenario,
    ImmutableArray<SampleData> Initial, ImmutableArray<SampleData> Late, ImmutableArray<SampleData> Extrema)
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
