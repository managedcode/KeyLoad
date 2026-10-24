using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphLabelUseClusterAuthority
{
    internal static async Task RunAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphLabelUseClusterState state, CancellationToken token)
    {
        await PersistAsync(admin, state, true, 1L, token);
        await GraphLabelUseClusterReads.RunAsync(reader, state, true, token);
        await CancelledAsync(reader, state);
        await PersistAsync(admin, state, false, 2L, token);
        await GraphLabelUseClusterReads.RunAsync(reader, state, false, token);
        await PersistAsync(admin, state, true, 3L, token);
        await GraphLabelUseClusterReads.RunAsync(reader, state, true, token);
    }

    private static async Task PersistAsync(RequestCqrsClusterCallers admin, GraphLabelUseClusterState state,
        bool allow, long advance, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = state.Reader.Principal.PolicyEpoch + advance,
            FieldGrants = allow ? [GraphLabelUseClusterProtocol.ReadGrant, GraphLabelUseClusterProtocol.UseGrant]
                : [GraphLabelUseClusterProtocol.ReadGrant]
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }

    private static async Task CancelledAsync(RequestCqrsClusterCallers reader, GraphLabelUseClusterState state)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var request = GraphLabelUseClusterProtocol.Request(state.Seed.Partition, ["route"]);
                var sql = SqlClusterProtocol.Call(request.Partition, McpCallerTools.GraphTraverse, request);
                var direct = await reader.Sdk.TraverseAsync(request, cancelled.Token);
                var sdkSql = await reader.Sdk.ExecuteSqlAsync(sql, cancelled.Token);
                await Assert.That(direct.IsFailed).IsTrue();
                await Assert.That(direct.Value).IsNull();
                await Assert.That(sdkSql.IsFailed).IsTrue();
                await Assert.That(sdkSql.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
                var expected = Errors.Problem(ErrorCode.Cancelled, "The read response is unavailable.");
                await SqlClusterProtocol.EqualAsync(expected, direct.Problem);
                await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.UnknownWriteOutcome,
                    "The write response is unavailable. Retry the same command ID."), sdkSql.Problem);
                var official = await Assert.ThrowsAsync<OperationCanceledException>(() => reader.Mcp.CallAsync(
                    McpCallerTools.GraphTraverse, request, cancelled.Token));
                var officialSql = await Assert.ThrowsAsync<OperationCanceledException>(() => reader.Mcp.CallAsync(
                    SqlOperationProtocol.ToolName, sql, cancelled.Token));
                await Assert.That(official?.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(officialSql?.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
