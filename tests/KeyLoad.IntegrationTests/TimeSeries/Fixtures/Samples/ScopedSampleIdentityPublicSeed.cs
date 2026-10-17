using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ScopedSampleIdentityPublicSeed
{
    private const string AppendKind = "appendSamples";
    internal static async Task<ScopedSampleIdentityPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, token);
        var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var state = new ScopedSampleIdentityPublicState(scenario, reader, placement);
        await AppendAsync(administrator, state, ScopedSampleIdentityPublicExpected.Original(scenario), token);
        await AppendAsync(administrator, state, ScopedSampleIdentityPublicExpected.Original(scenario), token);
        return state;
    }

    private static async Task AppendAsync(RequestCqrsRf3Callers administrator,
        ScopedSampleIdentityPublicState state, CommandRequest command, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, state.Placement,
            [new(AppendKind, TimeSeriesRf3Scenario.Set, ScopedSampleIdentityPublicExpected.A, 1L),
             new(AppendKind, TimeSeriesRf3Scenario.Set, ScopedSampleIdentityPublicExpected.B, 1L)]);
        state.Originals.Add((command, receipt));
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
    }
}
