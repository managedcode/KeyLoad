using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class ScopedSampleIdentityPublicSeed
{
    private const string AppendKind = "appendSamples";
    internal static async Task<ScopedSampleIdentityPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead, token);
        var state = new ScopedSampleIdentityPublicState(scenario, reader);
        await AppendAsync(administrator, state, ScopedSampleIdentityPublicExpected.Original(scenario), token);
        await AppendAsync(administrator, state, ScopedSampleIdentityPublicExpected.Original(scenario), token);
        return state;
    }

    private static async Task AppendAsync(RequestCqrsClusterCallers administrator,
        ScopedSampleIdentityPublicState state, CommandRequest command, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(AppendKind, TimeSeriesClusterScenario.Set, ScopedSampleIdentityPublicExpected.A, 1L),
             new(AppendKind, TimeSeriesClusterScenario.Set, ScopedSampleIdentityPublicExpected.B, 1L)]);
        state.Originals.Add((command, receipt));
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
    }
}
