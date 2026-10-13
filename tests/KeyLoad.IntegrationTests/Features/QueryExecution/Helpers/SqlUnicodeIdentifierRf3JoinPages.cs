namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierRf3JoinPages
{
    internal static async Task RequireAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire, CancellationToken token)
    {
        var sql = state.Dialect == PostgresRf3Protocol.JoinDialect ? JoinSql : ScalarSql;
        var request = state.Query with { Sql = sql };
        await PostgresRf3Assertions.PageAsync(state,
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(request, token)));
        await PostgresRf3Assertions.PageAsync(state,
            (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value);
        var ast = new global::KeyLoad.Query.AstQueryRequest(PostgresRf3State.Partition,
            Ast(state.Dialect), AllowFullScan: true, AstVersion: state.Dialect);
        await AstAsync(state, caller, ast, token);
        foreach (var prepared in new[] { false, true })
        { await PostgresRf3Assertions.PageAsync(state, await wire.ExecuteAsync<global::KeyLoad.QueryPage>(sql, [], prepared, token)); }
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
        { [Argument] = global::System.Text.Json.JsonSerializer.SerializeToElement(UnitValue) };
        var parameterSql = sql.Replace(OrderBy, NamedFilter + OrderBy, StringComparison.Ordinal);
        request = request with { Sql = parameterSql, Parameters = parameters };
        await PostgresRf3Assertions.PageAsync(state,
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(request, token)));
        await PostgresRf3Assertions.PageAsync(state,
            (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryExecute, request, token))).Value);
        var predicate = new global::KeyLoad.Query.Comparison(global::KeyLoad.Query.ValueOperand.Create(UnitValue), EqualityOperator,
            new global::KeyLoad.Query.ParameterOperand(Argument));
        var parameterAst = new global::KeyLoad.Query.AstQueryRequest(PostgresRf3State.Partition,
            Ast(state.Dialect) with { Filter = predicate }, parameters, AllowFullScan: true, AstVersion: state.Dialect);
        await AstAsync(state, caller, parameterAst, token);
        foreach (var prepared in new[] { false, true })
        {
            var parameter = new global::Npgsql.NpgsqlParameter
            { NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Numeric, Value = UnitValue };
            await PostgresRf3Assertions.PageAsync(state, await wire.ExecuteAsync<global::KeyLoad.QueryPage>(
                sql.Replace(OrderBy, PositionalFilter + OrderBy, StringComparison.Ordinal), [parameter], prepared, token));
        }
    }

    private static async Task AstAsync(PostgresRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        global::KeyLoad.Query.AstQueryRequest request, CancellationToken token)
    {
        await PostgresRf3Assertions.PageAsync(state,
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(request, token)));
        await PostgresRf3Assertions.PageAsync(state,
            (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<global::KeyLoad.QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, request, token))).Value);
    }

    private static global::KeyLoad.Query.SelectQuery Ast(int dialect)
        => dialect == PostgresRf3Protocol.JoinDialect
            ? new(LeftCollection, LeftAlias,
                [new(OrderPath, OrderOutput, SourceAlias: LeftAlias), new(DetailPath, DetailOutput, SourceAlias: LeftAlias),
                    new(NamePath, NameOutput, SourceAlias: RightAlias), new(IdPath, CustomerOutput, SourceAlias: RightAlias)],
                null, [new(OrderPath, false)], ResultLimit,
                InnerJoin: new(RightCollection, RightAlias, CustomerPath, IdPath))
            : new(LeftCollection, null, [new(Star, Star)], null, [new(OrderPath, false)], ResultLimit);

    private const string Argument = "argument";
    private const string EqualityOperator = "=";
    private const string OrderBy = " ORDER BY ";
    private const string NamedFilter = " WHERE 1 = @argument";
    private const string PositionalFilter = " WHERE 1 = $1";
    private const decimal UnitValue = 1m;
    private const int ResultLimit = 10;
    private const string LeftCollection = "budgetorders";
    private const string RightCollection = "budgetcustomers";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string OrderPath = "/order_id";
    private const string DetailPath = "/detail";
    private const string NamePath = "/name";
    private const string IdPath = "/id";
    private const string CustomerPath = "/customer_id";
    private const string OrderOutput = "order_id";
    private const string DetailOutput = "left_detail";
    private const string NameOutput = "right_name";
    private const string CustomerOutput = "customer_id";
    private const string Star = "*";
    private const string JoinSql = "SELECT U&\"\\006c\".U&\"order_\\0069d\" AS U&\"order_\\0069d\", U&\"\\006c\".U&\"d\\0065tail\" AS U&\"left_\\0064etail\", U&\"\\0072\".U&\"na\\006de\" AS U&\"right_na\\006de\", U&\"\\0072\".U&\"\\0069d\" AS U&\"customer_\\0069d\" FROM U&\"budget\\006frders\" AS U&\"\\006c\" INNER JOIN U&\"budgetcust\\006fmers\" AS U&\"\\0072\" ON U&\"\\006c\".U&\"customer_\\0069d\" = U&\"\\0072\".U&\"\\0069d\" ORDER BY U&\"\\006c\".U&\"order_\\0069d\" ASC LIMIT 10";
    private const string ScalarSql = "SELECT * FROM U&\"budget\\006frders\" ORDER BY U&\"order_\\0069d\" ASC LIMIT 10";
}
