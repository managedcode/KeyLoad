using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesFiniteOverflowPublicPreparation
{
    internal static async Task<SeriesFiniteOverflowPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, token);
        var placementRequest = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                    placementRequest, token))).Value);
        var seed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, SeriesFiniteOverflowPublicExpected.Series,
                [SeriesFiniteOverflowPublicExpected.First(), SeriesFiniteOverflowPublicExpected.Second()],
                SeriesFiniteOverflowPublicExpected.Tags)]);
        var seedReceipt = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(seed, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(seedReceipt, seed, placement,
            [new("appendSamples", TimeSeriesRf3Scenario.Set, SeriesFiniteOverflowPublicExpected.Series, 2L)]);
        var expiry = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, SeriesFiniteOverflowPublicExpected.Series,
                SeriesFiniteOverflowPublicExpected.SecondAt, 2)]);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new SeriesFiniteOverflowPublicState(scenario, reader, placement,
            seed, seedReceipt, expiry, outbox);
        await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
        return state;
    }
}
