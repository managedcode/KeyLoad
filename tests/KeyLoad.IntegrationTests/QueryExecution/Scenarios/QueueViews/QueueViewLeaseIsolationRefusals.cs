using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationRefusals
{
    internal static Task DeniedAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueueViewLeaseIsolationState state, CancellationToken token)
        => QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
        {
            await AllAsync(reader, state, state.Scenario.QueueSql(), state.Scenario.QueueAst(), ErrorCode.PermissionDenied, token);
            await QueueViewLeaseIsolationProblems.SdkAsync(await reader.Sdk.InspectAsync(
                state.Scenario.Inspect(SqlModelViewClusterScenario.FirstMessageId), token), ErrorCode.PermissionDenied);
        }, token);

    internal static async Task InputsAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueueViewLeaseIsolationState state, CancellationToken token)
    {
        await QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
        {
            var sql = state.Scenario.QueueSql() with
            {
                Sql = QueueViewLeaseIsolationProtocol.PrivateUseSql
            };
            var ast = state.Scenario.QueueAst() with
            {
                Query = state.Scenario.QueueAst().Query with
                {
                    Filter = new Comparison(new FieldOperand(QueueViewLeaseIsolationProtocol.PayloadPath),
                        QueueViewLeaseIsolationProtocol.EqualityOperator, ValueOperand.Create(SqlModelViewClusterScenario.QueueCanary))
                }
            };
            await AllAsync(reader, state, sql, ast, ErrorCode.PermissionDenied, token);
            await AllAsync(reader, state, state.Scenario.QueueSql(QueueViewLeaseIsolationProtocol.UnsupportedCursor),
                state.Scenario.QueueAst(QueueViewLeaseIsolationProtocol.UnsupportedCursor), ErrorCode.UnsupportedCapability, token);
            await AllAsync(root, state, state.Scenario.EventSql(generation: QueueViewLeaseIsolationProtocol.LeasedVersion),
                state.Scenario.EventAst(generation: QueueViewLeaseIsolationProtocol.LeasedVersion), ErrorCode.TokenInvalidated, token);
            var update = new SqlOperationRequest(state.Scenario.Partition, QueueViewLeaseIsolationProtocol.UpdateSql);
            await QueueViewLeaseIsolationProblems.SqlAsync(await reader.Sdk.ExecuteSqlAsync(update, token), ErrorCode.UnsupportedCapability);
            await QueueViewLeaseIsolationProblems.McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                update, token), state, ErrorCode.UnsupportedCapability, dispatched: false);
        }, token);
    }

    private static async Task AllAsync(RequestCqrsClusterCallers caller, QueueViewLeaseIsolationState state,
        QueryRequest sql, AstQueryRequest ast, ErrorCode code, CancellationToken token)
    {
        await QueueViewLeaseIsolationProblems.SdkAsync(await caller.Sdk.QueryAsync(sql, token), code);
        await QueueViewLeaseIsolationProblems.SdkAsync(await caller.Sdk.QueryAstAsync(ast, token), code);
        await QueueViewLeaseIsolationProblems.McpAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token), state, code);
        await QueueViewLeaseIsolationProblems.McpAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token), state, code);
        var operation = new SqlOperationRequest(state.Scenario.Partition, sql.Sql, sql.Parameters,
            AllowFullScan: sql.AllowFullScan, Cursor: sql.Cursor);
        await QueueViewLeaseIsolationProblems.SqlAsync(await caller.Sdk.ExecuteSqlAsync(operation, token), code);
        await QueueViewLeaseIsolationProblems.McpAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, token), state, code);
    }
}
