using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class CapabilityOutputRf3Authority
{
    internal static async Task RunAsync(KeyLoadClient root, RequestCqrsRf3Callers caller,
        CapabilityOutputRf3State state, int mode, CancellationToken token)
    {
        var before = await CapabilityOutputRf3Assertions.ImageAsync(root, state, token);
        await CancelledAsync(caller);
        await Assert.That(await CapabilityOutputRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(before);
        var denied = state.Reader.Principal with
        {
            PolicyEpoch = state.Reader.Principal.PolicyEpoch + 1L,
            Grants = [new(PostgresRf3State.Partition.DatabaseId, CapabilityOutputRf3Protocol.Collection,
                Capability.DocumentsRead)]
        };
        await SqlRf3Protocol.EqualAsync(denied, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        await CapabilityOutputRf3Pages.RefuseAsync(caller, true, 1, ErrorCode.PermissionDenied, token);
        await Assert.That(await CapabilityOutputRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(before);
        var repaired = state.Reader.Principal with
        {
            PolicyEpoch = state.Reader.Principal.PolicyEpoch + 2L
        };
        await SqlRf3Protocol.EqualAsync(repaired, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await CapabilityOutputRf3Pages.RunAsync(caller, state, mode, token);
        await Assert.That(await CapabilityOutputRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(before);
    }

    private static async Task CancelledAsync(RequestCqrsRf3Callers caller)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var sql = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Sdk.QueryAsync(
                    CapabilityOutputRf3Protocol.Sql(true), cancelled.Token));
                var ast = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Sdk.QueryAstAsync(
                    CapabilityOutputRf3Protocol.Ast(true), cancelled.Token));
                var officialSql = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, CapabilityOutputRf3Protocol.Sql(true), cancelled.Token));
                var officialAst = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Mcp.CallAsync(
                    McpCallerTools.QueryAst, CapabilityOutputRf3Protocol.Ast(true), cancelled.Token));
                await Assert.That(sql?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(ast?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(officialSql?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(officialAst?.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
