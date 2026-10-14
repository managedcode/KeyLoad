namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTableRf3Pages
{
    internal static async Task RequireAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, CancellationToken token)
    {
        foreach (var sql in new[] { TableSql, FetchSql, SelectSql })
        {
            var query = state.Query with { Sql = sql };
            await PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.QueryAsync(query, token)));
            await PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
                await reader.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, query, token))).Value);
            var operation = state.Operation with { Sql = sql };
            await PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, operation, token));
            await PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, operation, token));
            foreach (var prepared in new[] { false, true })
            { await PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared, token)); }
        }
        var ast = Ast(state);
        await PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await reader.Sdk.QueryAstAsync(ast, token)));
        await PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
            await reader.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value);
    }

    internal static global::KeyLoad.Query.AstQueryRequest Ast(PostgresRf3State state)
        => new(PostgresRf3State.Partition,
            new(Collection, null, [new(Star, Star)], null, [new(OrderPath, false)], ResultLimit),
            AllowFullScan: true, AstVersion: state.Dialect);

    internal static Task PageAsync(QueryPage actual)
        => SqlRf3Protocol.EqualAsync(new QueryPage(
            [new(PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision, PostgresRf3State.LeftJson,
                RedactedFields: [])], null, actual.CutPosition, FullScan), actual);

    internal const string TableSql = "SELECT TOP (10) * FROM budgetorders ORDER BY order_id";
    internal const string FetchSql = "SELECT * FROM budgetorders ORDER BY order_id OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY";
    internal const string SelectSql = "SELECT ALL TOP (10) * FROM budgetorders ORDER BY order_id";
    private const string Collection = "budgetorders";
    private const string Star = "*";
    private const string OrderPath = "/order_id";
    private const string FullScan = "bounded-full-scan";
    private const int ResultLimit = 10;
}
