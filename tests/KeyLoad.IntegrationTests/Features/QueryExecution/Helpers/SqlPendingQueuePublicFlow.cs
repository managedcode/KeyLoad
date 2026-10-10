using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlPendingQueuePublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        QueueLifecyclePublicState state, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, state.Partition, state.Lane.Queue,
            Capability.Query | Capability.QueueInspect, token);
        var principal = identity.Principal with { FieldGrants = ["*"], PolicyEpoch = identity.Principal.PolicyEpoch + QueueLifecyclePublicProtocol.One };
        principal = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await using var reader = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, token);
        var request = new QueryRequest(state.Partition,
            $"SELECT * FROM QUEUE_MESSAGES('{state.Lane.Queue}') ORDER BY id LIMIT {QueueLifecyclePublicProtocol.Two}", AllowFullScan: true);
        await DeniedAsync(reader, state, request, token);
        await SqlPendingQueuePublicAssertions.UnchangedAsync(administrator, state, token);
        principal = principal with
        {
            Grants = [new(state.Partition.DatabaseId, state.Lane.Queue,
            Capability.Query | Capability.QueueInspect | Capability.DeadLettersRead)],
            PolicyEpoch = principal.PolicyEpoch + QueueLifecyclePublicProtocol.One
        };
        principal = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var page = await QueueLifecyclePublicRoutes.CallAsync(reader, route, state.Partition, McpCallerTools.QueryExecute,
                request, () => reader.Sdk.QueryAsync(request, token), token);
            await SqlPendingQueuePublicAssertions.RowsAsync(administrator, state, page, token);
            var ast = Ast(state);
            var astPage = await QueueLifecyclePublicRoutes.CallAsync(reader, route, state.Partition, McpCallerTools.QueryAst,
                ast, () => reader.Sdk.QueryAstAsync(ast, token), token);
            await SqlPendingQueuePublicAssertions.RowsAsync(administrator, state, astPage, token);
        }
        await SqlPendingQueuePublicAssertions.UnchangedAsync(administrator, state, token);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal with
            {
                Grants = [new(state.Partition.DatabaseId, state.Lane.Queue, Capability.Query | Capability.QueueInspect)],
                PolicyEpoch = principal.PolicyEpoch + QueueLifecyclePublicProtocol.One
            }, token));
        await DeniedAsync(reader, state, request, token);
        await SqlPendingQueuePublicAssertions.UnchangedAsync(administrator, state, token);
    }

    private static async Task DeniedAsync(RequestCqrsRf3Callers reader, QueueLifecyclePublicState state,
        QueryRequest request, CancellationToken token)
    {
        await DeniedRequestAsync(reader, state, McpCallerTools.QueryExecute, request,
            () => reader.Sdk.QueryAsync(request, token), token);
        var ast = Ast(state);
        await DeniedRequestAsync(reader, state, McpCallerTools.QueryAst, ast,
            () => reader.Sdk.QueryAstAsync(ast, token), token);
    }

    private static AstQueryRequest Ast(QueueLifecyclePublicState state)
        => new(state.Partition, new(state.Lane.Queue, null, [new("*", "*")], null,
            [new("/@id", false)], QueueLifecyclePublicProtocol.Two, false,
            new(ModelQuerySourceKind.QueueMessages, state.Lane.Queue)), AllowFullScan: true);

    private static async Task DeniedRequestAsync(RequestCqrsRf3Callers reader, QueueLifecyclePublicState state,
        string tool, object request, Func<Task<ManagedCode.Communication.Result<QueryPage>>> execute, CancellationToken token)
    {
        var sdk = await execute();
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(sdk, ErrorCode.PermissionDenied);
        await Assert.That(sdk.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(tool, request, token), ErrorCode.PermissionDenied, dispatched: true);
        var sql = SqlRf3Protocol.Call(state.Partition, tool, request);
        var q1 = await reader.Sdk.ExecuteSqlAsync(sql, token);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(q1, ErrorCode.PermissionDenied);
        await Assert.That(q1.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), ErrorCode.PermissionDenied, dispatched: true);
    }
}
