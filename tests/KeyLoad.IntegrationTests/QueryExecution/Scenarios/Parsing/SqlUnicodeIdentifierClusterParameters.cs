namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierClusterParameters
{
    internal static async Task RequireAsync(
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, long revision, CancellationToken token)
    {
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
        {
            [Argument] = global::System.Text.Json.JsonSerializer.SerializeToElement(UnitValue)
        };
        var item = new SqlNullDistinctClusterCase(Clause, new global::KeyLoad.Query.Logical(
            new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), "AND",
            new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Argument), "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue))), [Low]);
        var request = new global::KeyLoad.QueryRequest(PostgresClusterState.Partition,
            ProjectionPrefix + Clause + SqlNullDistinctClusterCases.Tail, parameters, AllowFullScan: true);
        await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAsync(request, token)), item, revision);
        await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
        var ast = SqlNullDistinctClusterCases.Ast(PostgresClusterState.Partition, item) with
        {
            Parameters = parameters
        };
        await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        foreach (var prepared in new[] { false, true })
        {
            var argument = new global::Npgsql.NpgsqlParameter
            {
                NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Numeric,
                Value = UnitValue
            };
            await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                ProjectionPrefix + WireClause + SqlNullDistinctClusterCases.Tail, [argument], prepared, token), item, revision);
        }
        await WindowsAsync(caller, wire, revision, token);
    }

    private static async Task WindowsAsync(
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, long revision, CancellationToken token)
    {
        var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlUnicodeIdentifierCases.Clause);
        foreach (var window in new[] { "LIMIT 5 OFFSET 0", "OFFSET 0 ROWS FETCH FIRST 5 ROWS ONLY" })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + item.Clause + OrderBy + window;
            var request = new global::KeyLoad.QueryRequest(PostgresClusterState.Partition, sql, AllowFullScan: true);
            await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAsync(request, token)), item, revision);
            await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
            foreach (var prepared in new[] { false, true })
            {
                await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                sql, [], prepared, token), item, revision);
            }
        }
    }

    private const string ProjectionPrefix = "SELECT U&\"la\\0062el\" AS U&\"la\\0062el\" FROM U&\"null_\\006frdering\" WHERE ";
    private const string Argument = "argument";
    private const string Low = "low";
    private const string OrderBy = " ORDER BY label ASC ";
    private const string Clause = "U&\"sc\\006fre\" = 1 AND @argument = 1";
    private const string WireClause = "U&\"sc\\006fre\" = 1 AND $1 = 1";
    private const decimal UnitValue = 1m;
}
