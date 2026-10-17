using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record TimeSeriesCurrentCapabilityRf3State(TimeSeriesRf3Scenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt, CommandRequest ReaderCommand, CommitReceipt ReaderReceipt,
    CommandRequest? FreshCommand = null, CommitReceipt? FreshReceipt = null)
{
    internal ReadSamplesRequest Range() => new(Scenario.Partition, TimeSeriesRf3Scenario.Set,
        TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddTicks(FreshCommand is null ? 2 : 3), 4);
    internal AggregateSamplesRequest Aggregate() => Scenario.Aggregate(TimeSeriesRf3Scenario.Start,
        TimeSeriesRf3Scenario.Start.AddTicks(FreshCommand is null ? 3 : 4), 4);
}
