namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBooleanOperandClusterParameters
{
    private const string Flag = "flag";
    private const string Score = "/score";
    private const string Low = "low";
    private const string Clause = "score = @truth AND @flag = @truth";
    private const string WireClause = "score = $1 AND $2 = $1";

    internal static async Task RequireAsync(
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, long revision, CancellationToken token)
    {
        foreach (var value in new bool?[] { true, false, null })
        {
            var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
            {
                [Flag] = global::System.Text.Json.JsonSerializer.SerializeToElement(value),
                [SqlBooleanTruthClusterCases.TrueParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(true)
            };
            var predicate = new global::KeyLoad.Query.Logical(
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.FieldOperand(Score), "=",
                    global::KeyLoad.Query.ValueOperand.Create(true)), "AND",
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Flag), "=",
                    global::KeyLoad.Query.ValueOperand.Create(true)));
            var item = new SqlBooleanTruthClusterCase(() => new(SqlBooleanTruthClusterCases.Prefix + Clause + SqlBooleanTruthClusterCases.Tail), predicate, value == true ? [Low] : []);
            var sql = SqlBooleanTruthClusterCases.Prefix + Clause + SqlBooleanTruthClusterCases.Tail;
            var request = new global::KeyLoad.QueryRequest(PostgresClusterState.Partition, sql, parameters, AllowFullScan: true);
            await SqlBooleanTruthClusterImage.PageAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAsync(request, token)), item, revision);
            await SqlBooleanTruthClusterImage.PageAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
            var ast = SqlBooleanTruthClusterCases.Ast(PostgresClusterState.Partition, item) with
            {
                Parameters = parameters
            };
            await SqlBooleanTruthClusterImage.PageAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
            await SqlBooleanTruthClusterImage.PageAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
            foreach (var prepared in new[] { false, true })
            {
                var argument = new global::Npgsql.NpgsqlParameter
                {
                    NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Boolean,
                    Value = value.HasValue ? value.Value : global::System.DBNull.Value
                };
                await SqlBooleanTruthClusterImage.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                    () => new(SqlBooleanTruthClusterCases.Prefix + WireClause + SqlBooleanTruthClusterCases.Tail),
                    [new() { NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Boolean, Value = true }, argument], prepared, token), item, revision);
            }
        }
    }
}
