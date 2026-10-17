namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNumericSeparatorRf3JoinParameters
{
    internal static async Task RequireAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, CancellationToken token)
    {
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
        {
            [Argument] = global::System.Text.Json.JsonSerializer.SerializeToElement(UnitValue)
        };
        var sql = state.Sql.Replace(OrderBy, " WHERE 1_0e-1 = @argument" + OrderBy, StringComparison.Ordinal);
        await global::TUnit.Assertions.Assert.That(sql).IsNotEqualTo(state.Sql);
        var request = state.Query with
        {
            Sql = sql,
            Parameters = parameters
        };
        await PostgresRf3Assertions.PageAsync(state,
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token)));
        await PostgresRf3Assertions.PageAsync(state,
            (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await reader.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value);
        var wireSql = state.Sql.Replace(OrderBy, " WHERE 1_0e-1 = $1 AND 0x_1 = 0b_1 AND 0o_1 = 1" + OrderBy, StringComparison.Ordinal);
        foreach (var prepared in new[] { false, true })
        {
            var argument = new global::Npgsql.NpgsqlParameter
            {
                NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Numeric,
                Value = UnitValue
            };
            await PostgresRf3Assertions.PageAsync(state,
                await wire.ExecuteAsync<global::KeyLoad.QueryPage>(wireSql, [argument], prepared, token));
        }
    }

    private const string Argument = "argument";
    private const string OrderBy = " ORDER BY ";
    private const decimal UnitValue = 1m;
}
