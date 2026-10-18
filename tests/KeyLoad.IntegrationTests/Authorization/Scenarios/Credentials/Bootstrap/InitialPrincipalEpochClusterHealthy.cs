using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochClusterHealthy
{
    internal static async Task<InitialPrincipalEpochClusterHealthyState> CreateAsync(ClusterFixture fixture,
        InitialPrincipalEpochClusterState state, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.ReplacementSecret, async reader =>
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(InitialPrincipalEpochClusterState.Collection, "record", "{\"value\":7}", 1L)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
            await InitialPrincipalEpochClusterValues.ReceiptAsync(command, receipt, 2L);
            return new InitialPrincipalEpochClusterHealthyState(command, receipt);
        }, token);
    internal static async Task WaveAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        InitialPrincipalEpochClusterState state, InitialPrincipalEpochClusterHealthyState healthy, CancellationToken token)
    {
        await InitialPrincipalEpochClusterRefusals.InitialAsync(root, state, 2L, token);
        await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 2L, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Principal(1L), await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(state.PrincipalCommandId, state.Principal(1L), token)));
            var unchanged = await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Principal(4L), token);
            await Assert.That(unchanged.IsFailed).IsTrue();
            await Assert.That(unchanged.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
                "A policy update must advance its epoch."), unchanged.Problem);
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.ReplacementSecret, async reader =>
            {
                await InitialPrincipalEpochClusterValues.DocumentAsync(reader, state, 2L, token);
                await SqlClusterProtocol.EqualAsync(healthy.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                    await reader.Sdk.CommitAsync(healthy.Command, token)));
                await SqlClusterProtocol.EqualAsync(healthy.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, healthy.Command, token))).Value);
                await InitialPrincipalEpochClusterValues.DocumentAsync(reader, state, 2L, token);
                return true;
            }, token);
            await SqlClusterProtocol.EqualAsync(state.SeedReceipt, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CommitAsync(state.Seed, token)));
        }, token);
    }
}
