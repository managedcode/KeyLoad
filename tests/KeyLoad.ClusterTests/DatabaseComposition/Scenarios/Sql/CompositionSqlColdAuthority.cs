using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.RelationalStorage;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class CompositionSqlColdAuthority
{
    internal static Task<(McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt, global::KeyLoad.GraphTraversal Graph)> RunAsync(
        ClusterFixture fixture, CompositionSqlColdState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
    {
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(state.Scenario.Partition.TenantId,
            state.Scenario.Partition.DatabaseId, new(CompositionSqlColdProtocol.OtherGraph, ResourceKind.Graph, state.Scenario.Partition.TransactionDomainId)), token));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, state.Scenario.Partition,
            CompositionSqlColdProtocol.OtherGraph, Capability.GraphRead, token);
        var principal = await GrantAsync(admin, state, identity.Principal, false, token);
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, async callers =>
        {
            var refused = Command(state);
            await DeniedAsync(callers, refused, token);
            await CompositionSqlColdAssertions.OtherAsync(admin, state, false, token);
            await CompositionSqlColdAssertions.OriginalAsync(admin, state, token);
            await GrantAsync(admin, state, principal, true, token);
            await DeniedAsync(callers, refused, token);
            var healthy = Command(state);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(healthy, token));
            await Assert.That(receipt.Mutations.Length).IsEqualTo(CompositionSqlColdProtocol.HealthyEffects);
            await CompositionSqlColdAssertions.ReplayAsync(callers, healthy, receipt, token);
            await CompositionSqlColdAssertions.OtherAsync(admin, state, true, token);
            await CompositionSqlColdAssertions.OriginalAsync(admin, state, token);
            var graph = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.TraverseAsync(new(state.Scenario.Partition,
                CompositionSqlColdProtocol.OtherGraph, state.Scenario.First), token));
            return (identity, healthy, receipt, graph);
        }, token);
    }, token);

    private static CommandRequest Command(CompositionSqlColdState state) => state.Scenario.Command(
        new PutDocument(RelationalSqlClusterTokens.Documents, CompositionSqlColdProtocol.Marker, RelationalSqlClusterTokens.EmptyJson),
        new QueueToGraph(CompositionSqlColdProtocol.OtherGraph, RelationalSqlClusterTokens.Queue, CompositionSqlColdProtocol.HealthyPrefix));

    private static async Task<PrincipalRecord> GrantAsync(RequestCqrsClusterCallers admin, CompositionSqlColdState state,
        PrincipalRecord original, bool graphWrite, CancellationToken token)
    {
        var partition = state.Scenario.Partition;
        var changed = original with
        {
            PolicyEpoch = original.PolicyEpoch + CompositionSqlColdProtocol.FirstEpoch,
            Grants = [new(partition.DatabaseId, RelationalSqlClusterTokens.Table, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, RelationalSqlClusterTokens.Documents, Capability.DocumentsWrite | Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, RelationalSqlClusterTokens.Queue, Capability.QueueInspect | Capability.Query),
                new(partition.DatabaseId, CompositionSqlColdProtocol.OtherGraph, Capability.GraphRead | Capability.Query
                    | (graphWrite ? Capability.GraphWrite : Capability.None))]
        };
        return await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), changed, token));
    }

    private static async Task DeniedAsync(RequestCqrsClusterCallers callers, CommandRequest command, CancellationToken token)
    {
        var sdk = await callers.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command) with
        {
            CommandId = command.CommandId
        };
        var q1 = await callers.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(q1.IsFailed).IsTrue();
        await Assert.That(q1.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
    }
}
