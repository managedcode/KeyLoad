namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanOperandRf3Parameters
{
    private const string Flag = "flag";
    private const string Score = "/score";
    private const string Low = "low";
    private const string Clause = "score AND @flag";
    private const string WireClause = "score AND $1";

    internal static async Task RequireAsync(
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire, long revision, CancellationToken token)
    {
        foreach (var value in new bool?[] { true, false, null })
        {
            var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
            {
                [Flag] = global::System.Text.Json.JsonSerializer.SerializeToElement(value)
            };
            var predicate = new global::KeyLoad.Query.Logical(
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.FieldOperand(Score), "=",
                    global::KeyLoad.Query.ValueOperand.Create(true)), "AND",
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Flag), "=",
                    global::KeyLoad.Query.ValueOperand.Create(true)));
            var item = new SqlBooleanTruthRf3Case(Clause, predicate, value == true ? [Low] : []);
            var sql = SqlBooleanTruthRf3Cases.Prefix + Clause + SqlBooleanTruthRf3Cases.Tail;
            var request = new global::KeyLoad.QueryRequest(PostgresRf3State.Partition, sql, parameters, AllowFullScan: true);
            await SqlBooleanTruthRf3Image.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAsync(request, token)), item, revision);
            await SqlBooleanTruthRf3Image.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value, item, revision);
            var ast = SqlBooleanTruthRf3Cases.Ast(PostgresRf3State.Partition, item) with
            {
                Parameters = parameters
            };
            await SqlBooleanTruthRf3Image.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
            await SqlBooleanTruthRf3Image.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
            foreach (var prepared in new[] { false, true })
            {
                var argument = new global::Npgsql.NpgsqlParameter
                {
                    NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Boolean,
                    Value = value.HasValue ? value.Value : global::System.DBNull.Value
                };
                await SqlBooleanTruthRf3Image.PageAsync(await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                    SqlBooleanTruthRf3Cases.Prefix + WireClause + SqlBooleanTruthRf3Cases.Tail,
                    [argument], prepared, token), item, revision);
            }
        }
    }
}
