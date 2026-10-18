using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesRetentionAtomicPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var prepared = await SeriesRetentionAtomicPublicPreparation.CreateAsync(fixture, administrator, token);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                    prepared.Reader.Secret, async reader =>
                    {
                        await CommitAsync(administrator, prepared, prepared.Seed,
                            SeriesRetentionAtomicPublicExpected.AppendKind, 2L, false, token);
                        await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, prepared, token);
                        await CommitAsync(administrator, prepared, prepared.Expiry,
                            SeriesRetentionAtomicPublicExpected.ExpireKind, 1L, false, token);
                        prepared.Purged = true;
                        await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, prepared, token);
                        await CommitAsync(administrator, prepared, prepared.EchoSdk,
                            SeriesRetentionAtomicPublicExpected.AppendKind, 2L, false, token);
                        await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, prepared, token);
                        await CommitAsync(administrator, prepared, prepared.EchoMcp,
                            SeriesRetentionAtomicPublicExpected.AppendKind, 2L, true, token);
                        await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, prepared, token);
                        await SeriesRetentionAtomicPublicRefusals.AllAsync(administrator, reader, prepared, token);
                        await SeriesRetentionAtomicPublicReplay.AllAsync(administrator, reader, prepared, token);
                        return prepared;
                    }, token);
            }, token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, administrator => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
                McpCallerProtocol.Node3, state.Reader.Secret, async reader =>
                {
                    await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
                    await SeriesRetentionAtomicPublicRefusals.AllAsync(administrator, reader, state, token);
                    await SeriesRetentionAtomicPublicReplay.AllAsync(administrator, reader, state, token);
                    await CommitAsync(administrator, state, state.Fresh,
                        SeriesRetentionAtomicPublicExpected.AppendKind, 3L, true, token);
                    state.Advanced = true;
                    await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
                    await SeriesRetentionAtomicPublicReplay.AllAsync(administrator, reader, state, token);
                    return true;
                }, token), token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, administrator => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
                McpCallerProtocol.Node3, state.Reader.Secret, async reader =>
                {
                    await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
                    await SeriesRetentionAtomicPublicRefusals.AllAsync(administrator, reader, state, token);
                    await SeriesRetentionAtomicPublicReplay.AllAsync(administrator, reader, state, token);
                    return true;
                }, token), token);
    }

    private static async Task CommitAsync(RequestCqrsClusterCallers administrator, SeriesRetentionAtomicPublicState state,
        CommandRequest command, string kind, long revision, bool mcpFirst, CancellationToken token)
    {
        var receipt = mcpFirst
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(kind, TimeSeriesClusterScenario.Set, SeriesRetentionAtomicPublicExpected.Series, revision)]);
        state.Originals.Add((command, receipt));
        state.CurrentOutbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
    }
}
