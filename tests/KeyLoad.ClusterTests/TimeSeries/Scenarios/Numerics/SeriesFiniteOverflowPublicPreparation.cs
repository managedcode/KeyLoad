using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SeriesFiniteOverflowPublicPreparation
{
    internal static async Task<SeriesFiniteOverflowPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead, token);
        var seed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, SeriesFiniteOverflowPublicExpected.Series,
                [SeriesFiniteOverflowPublicExpected.First(), SeriesFiniteOverflowPublicExpected.Second()],
                SeriesFiniteOverflowPublicExpected.Tags)]);
        var seedReceipt = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(seed, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(seedReceipt, seed, [new("appendSamples", TimeSeriesClusterScenario.Set, SeriesFiniteOverflowPublicExpected.Series, 2L)]);
        var expiry = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new ExpireSamples(TimeSeriesClusterScenario.Set, SeriesFiniteOverflowPublicExpected.Series,
                SeriesFiniteOverflowPublicExpected.SecondAt, 2)]);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new SeriesFiniteOverflowPublicState(scenario, reader, seed, seedReceipt, expiry, outbox);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        return state;
    }
}
