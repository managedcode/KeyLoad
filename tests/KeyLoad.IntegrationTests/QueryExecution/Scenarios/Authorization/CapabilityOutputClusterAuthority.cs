using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class CapabilityOutputClusterAuthority
{
    internal static async Task RunAsync(KeyLoadClient root, RequestCqrsClusterCallers caller,
        CapabilityOutputClusterState state, int mode, CancellationToken token)
    {
        var before = await CapabilityOutputClusterAssertions.ImageAsync(root, state, token);
        await CancelledAsync(caller);
        await Assert.That(await CapabilityOutputClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(before);
        var denied = state.Reader.Principal with
        {
            PolicyEpoch = state.Reader.Principal.PolicyEpoch + 1L,
            Grants = [new(PostgresClusterState.Partition.DatabaseId, CapabilityOutputClusterProtocol.Collection,
                Capability.DocumentsRead)]
        };
        await SqlClusterProtocol.EqualAsync(denied, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        await CapabilityOutputClusterPages.RefuseAsync(caller, true, 1, ErrorCode.PermissionDenied, token);
        await Assert.That(await CapabilityOutputClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(before);
        var repaired = state.Reader.Principal with
        {
            PolicyEpoch = state.Reader.Principal.PolicyEpoch + 2L
        };
        await SqlClusterProtocol.EqualAsync(repaired, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await CapabilityOutputClusterPages.RunAsync(caller, state, mode, token);
        await Assert.That(await CapabilityOutputClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(before);
    }

    private static async Task CancelledAsync(RequestCqrsClusterCallers caller)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var sql = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Sdk.QueryAsync(
                    CapabilityOutputClusterProtocol.Sql(true), cancelled.Token));
                var ast = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Sdk.QueryAstAsync(
                    CapabilityOutputClusterProtocol.Ast(true), cancelled.Token));
                var officialSql = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, CapabilityOutputClusterProtocol.Sql(true), cancelled.Token));
                var officialAst = await Assert.ThrowsAsync<OperationCanceledException>(() => caller.Mcp.CallAsync(
                    McpCallerTools.QueryAst, CapabilityOutputClusterProtocol.Ast(true), cancelled.Token));
                await Assert.That(sql?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(ast?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(officialSql?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(officialAst?.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
