namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierRf3Parameters
{
    internal static async Task RequireAsync(
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire, long revision, CancellationToken token)
    {
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
        {
            [Argument] = global::System.Text.Json.JsonSerializer.SerializeToElement(UnitValue)
        };
        var item = new SqlNullDistinctRf3Case(Clause, new global::KeyLoad.Query.Logical(
            new global::KeyLoad.Query.Comparison(SqlNullDistinctRf3Cases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), "AND",
            new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Argument), "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue))), [Low]);
        var request = new global::KeyLoad.QueryRequest(PostgresRf3State.Partition,
            ProjectionPrefix + Clause + SqlNullDistinctRf3Cases.Tail, parameters, AllowFullScan: true);
        await SqlNullDistinctRf3Image.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAsync(request, token)), item, revision);
        await SqlNullDistinctRf3Image.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
        var ast = SqlNullDistinctRf3Cases.Ast(PostgresRf3State.Partition, item) with
        {
            Parameters = parameters
        };
        await SqlNullDistinctRf3Image.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlNullDistinctRf3Image.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        foreach (var prepared in new[] { false, true })
        {
            var argument = new global::Npgsql.NpgsqlParameter
            {
                NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Numeric,
                Value = UnitValue
            };
            await SqlNullDistinctRf3Image.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                ProjectionPrefix + WireClause + SqlNullDistinctRf3Cases.Tail, [argument], prepared, token), item, revision);
        }
        await WindowsAsync(caller, wire, revision, token);
    }

    private static async Task WindowsAsync(
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire, long revision, CancellationToken token)
    {
        var item = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlUnicodeIdentifierCases.Clause);
        foreach (var window in new[] { "LIMIT 5 OFFSET 0", "OFFSET 0 ROWS FETCH FIRST 5 ROWS ONLY" })
        {
            var sql = SqlNullDistinctRf3Cases.Prefix + item.Clause + OrderBy + window;
            var request = new global::KeyLoad.QueryRequest(PostgresRf3State.Partition, sql, AllowFullScan: true);
            await SqlNullDistinctRf3Image.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAsync(request, token)), item, revision);
            await SqlNullDistinctRf3Image.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
            foreach (var prepared in new[] { false, true })
            {
                await SqlNullDistinctRf3Image.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
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
