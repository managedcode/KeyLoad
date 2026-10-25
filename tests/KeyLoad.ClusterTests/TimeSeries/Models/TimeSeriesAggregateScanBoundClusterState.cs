using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed record TimeSeriesAggregateScanBoundClusterState(TimeSeriesClusterScenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt,
    CommandRequest? FreshCommand = null, CommitReceipt? FreshReceipt = null)
{
    internal ReadSamplesRequest Range(int limit = 4) => new(Scenario.Partition,
        TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Start,
        TimeSeriesClusterScenario.Start.AddTicks(FreshCommand is null ? 2 : 3), limit);
    internal AggregateSamplesRequest Aggregate(int limit = 4) => Scenario.Aggregate(
        TimeSeriesClusterScenario.Start, TimeSeriesClusterScenario.Start.AddTicks(FreshCommand is null ? 3 : 4), limit);
}
