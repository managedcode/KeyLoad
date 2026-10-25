using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNumericSeparatorClusterParameters
{
    internal static async Task RequireAsync(
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, long revision, CancellationToken token)
    {
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
        {
            [Argument] = global::System.Text.Json.JsonSerializer.SerializeToElement(UnitValue)
        };
        var item = new SqlNullDistinctClusterCase(() => new(SqlNullDistinctClusterCases.Prefix + Clause + SqlNullDistinctClusterCases.Tail), new global::KeyLoad.Query.Logical(
            new global::KeyLoad.Query.Comparison(SqlNullDistinctClusterCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), "AND",
            new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Argument), "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue))), [Low]);
        var request = new global::KeyLoad.QueryRequest(PostgresClusterState.Partition,
            SqlNullDistinctClusterCases.Prefix + Clause + SqlNullDistinctClusterCases.Tail, parameters, AllowFullScan: true);
        await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAsync(request, token)), item, revision);
        await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
        var ast = SqlNullDistinctClusterCases.Ast(PostgresClusterState.Partition, item) with
        {
            Parameters = parameters
        };
        await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        foreach (var prepared in new[] { false, true })
        {
            var argument = new global::Npgsql.NpgsqlParameter
            {
                NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Numeric,
                Value = UnitValue
            };
            await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                () => new(SqlNullDistinctClusterCases.Prefix + WireClause + SqlNullDistinctClusterCases.Tail), [argument], prepared, token), item, revision);
        }
        await WindowsAsync(caller, wire, revision, token);
    }

    private static async Task WindowsAsync(
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, long revision, CancellationToken token)
    {
        var item = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.NumericSeparatorClause);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "OFFSET 0 ROWS FETCH FIRST 5 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.NumericSeparatorClause + OrderBy + "OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY")
        })
        {
            var sql = PostgresClusterCommands.Sql(command);
            var request = new global::KeyLoad.QueryRequest(PostgresClusterState.Partition, sql, AllowFullScan: true);
            await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAsync(request, token)), item, revision);
            await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
            foreach (var prepared in new[] { false, true })
            {
                await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                command, [], prepared, token), item, revision);
            }
        }
    }

    private const string Argument = "argument";
    private const string Low = "low";
    private const string OrderBy = " ORDER BY label ASC ";
    private const string Clause = "score = 1 AND @argument = 1";
    private const string WireClause = "score = 1 AND $1 = 1";
    private const decimal UnitValue = 1m;
}
