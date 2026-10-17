using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochRf3Healthy
{
    internal static async Task<InitialPrincipalEpochRf3HealthyState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, InitialPrincipalEpochRf3State state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.ReplacementSecret, async reader =>
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(InitialPrincipalEpochRf3State.Collection, "record", "{\"value\":7}", 1L)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
            await InitialPrincipalEpochRf3Values.ReceiptAsync(root, command, receipt, 2L, token);
            return new InitialPrincipalEpochRf3HealthyState(command, receipt);
        }, token);
    internal static async Task WaveAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        InitialPrincipalEpochRf3State state, InitialPrincipalEpochRf3HealthyState healthy, CancellationToken token)
    {
        await InitialPrincipalEpochRf3Refusals.InitialAsync(root, state, 2L, token);
        await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 2L, async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Principal(1L), await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(state.PrincipalCommandId, state.Principal(1L), token)));
            var unchanged = await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Principal(4L), token);
            await Assert.That(unchanged.IsFailed).IsTrue();
            await Assert.That(unchanged.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict,
                "A policy update must advance its epoch."), unchanged.Problem);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.ReplacementSecret, async reader =>
            {
                await InitialPrincipalEpochRf3Values.DocumentAsync(reader, state, 2L, token);
                await SqlRf3Protocol.EqualAsync(healthy.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                    await reader.Sdk.CommitAsync(healthy.Command, token)));
                await SqlRf3Protocol.EqualAsync(healthy.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, healthy.Command, token))).Value);
                await InitialPrincipalEpochRf3Values.DocumentAsync(reader, state, 2L, token);
                return true;
            }, token);
            await SqlRf3Protocol.EqualAsync(state.SeedReceipt, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CommitAsync(state.Seed, token)));
        }, token);
    }
}
