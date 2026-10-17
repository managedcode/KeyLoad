using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record TimeSeriesAggregateScanBoundRf3State(TimeSeriesRf3Scenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt,
    CommandRequest? FreshCommand = null, CommitReceipt? FreshReceipt = null)
{
    internal ReadSamplesRequest Range(int limit = 4) => new(Scenario.Partition,
        TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Start,
        TimeSeriesRf3Scenario.Start.AddTicks(FreshCommand is null ? 2 : 3), limit);
    internal AggregateSamplesRequest Aggregate(int limit = 4) => Scenario.Aggregate(
        TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddTicks(FreshCommand is null ? 3 : 4), limit);
}
