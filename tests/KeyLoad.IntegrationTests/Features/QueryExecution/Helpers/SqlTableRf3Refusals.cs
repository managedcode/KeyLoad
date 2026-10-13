namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTableRf3Refusals
{
    internal static async Task RequireAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, global::KeyLoad.Client.KeyLoadClient root, CancellationToken token)
    {
        var image = await PostgresRf3Assertions.ImageAsync(root, token);
        foreach (var sql in new[] { "TABLE", "TABLE budgetorders LIMIT 0", "TABLE budgetorders OFFSET -1 LIMIT 1",
            "TABLE budgetorders LIMIT 2147483648", "TABLE budgetorders FETCH FIRST '1' ROWS ONLY" })
        {
            await RejectAsync(state, reader, wire, sql, ErrorCode.Validation, ValidationState, token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(image);
            await SqlTableRf3Pages.RequireAsync(state, reader, wire, token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(image);
        }
        foreach (var sql in new[] { "TABLE budgetorders WHERE order_id = 'order-native'", "TABLE budgetorders AS l",
            "TABLE budgetorders JOIN budgetcustomers ON customer_id = id", "TABLE budgetorders UNION TABLE budgetcustomers",
            "TABLE budgetorders, budgetcustomers", "TABLE budgetorders FOR UPDATE", "TABLE budgetorders GROUP BY order_id" })
        {
            await RejectAsync(state, reader, wire, sql, ErrorCode.UnsupportedCapability,
                PostgresRf3Protocol.UnsupportedSqlState, token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(image);
            await SqlTableRf3Pages.RequireAsync(state, reader, wire, token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(image);
        }
    }

    internal static async Task DeniedAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, CancellationToken token)
    {
        await RejectAsync(state, reader, wire, SqlTableRf3Pages.TableSql,
            ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
        var ast = SqlTableRf3Pages.Ast(state);
        var sdk = await reader.Sdk.QueryAstAsync(ast, token);
        await global::TUnit.Assertions.Assert.That(sdk.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(sdk.Value).IsNull();
        await global::TUnit.Assertions.Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(
            await reader.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst,
                ast, token), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task RejectAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, string sql, ErrorCode code, string sqlState, CancellationToken token)
    {
        var query = state.Query with { Sql = sql };
        var sdk = await reader.Sdk.QueryAsync(query, token);
        await global::TUnit.Assertions.Assert.That(sdk.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(sdk.Value).IsNull();
        await global::TUnit.Assertions.Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(
            await reader.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute,
                query, token), code, dispatched: true);
        var operation = state.Operation with { Sql = sql };
        var executed = await reader.Sdk.ExecuteSqlAsync(operation, token);
        await global::TUnit.Assertions.Assert.That(executed.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(executed.Value.ValueKind)
            .IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
        await global::TUnit.Assertions.Assert.That(executed.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(
            await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                operation, token), code, dispatched: true);
        foreach (var prepared in new[] { false, true })
        { await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared, token), sqlState, code); }
    }

    private const string ValidationState = "22023";
}
