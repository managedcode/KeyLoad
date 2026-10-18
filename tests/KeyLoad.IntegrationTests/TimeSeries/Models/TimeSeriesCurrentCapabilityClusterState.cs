using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record TimeSeriesCurrentCapabilityClusterState(TimeSeriesClusterScenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt, CommandRequest ReaderCommand, CommitReceipt ReaderReceipt,
    CommandRequest? FreshCommand = null, CommitReceipt? FreshReceipt = null)
{
    internal ReadSamplesRequest Range() => new(Scenario.Partition, TimeSeriesClusterScenario.Set,
        TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start.AddTicks(FreshCommand is null ? 2 : 3), 4);
    internal AggregateSamplesRequest Aggregate() => Scenario.Aggregate(TimeSeriesClusterScenario.Start,
        TimeSeriesClusterScenario.Start.AddTicks(FreshCommand is null ? 3 : 4), 4);
}
