using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlImplicitAliasClusterPages
{
    internal const string BasicSql = "SELECT TOP (5) score projected_score FROM null_ordering WHERE score IS NULL ORDER BY label ASC";
    private const string Prefix = "SELECT TOP (5) score ";
    private const string Tail = " FROM null_ordering WHERE score IS NULL ORDER BY label ASC";
    private static readonly (string Label, string Name, string Json)[] Cases =
    [
        ("projected_score", "projected_score", "{\"projected_score\":null}"),
        ("\"ORDER\"", "ORDER", "{\"ORDER\":null}"),
        ("\"FROM\"", "FROM", "{\"FROM\":null}")
    ];

    internal static async Task HealthyAsync(global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, CancellationToken token)
    {
        foreach (var (label, name, json) in Cases)
        {
            foreach (var sql in new[] { Prefix + label + Tail, Prefix + ExplicitAs + label + Tail })
            {
                var operation = SqlNullOrderingClusterProtocol.Operation(sql);
                await EqualAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), json);
                await EqualAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token), json);
                await EqualAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), json);
                await EqualAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), json);
            }
            var ast = new AstQueryRequest(PostgresClusterState.Partition,
                new(SqlNullOrderingClusterProtocol.Collection, null, [new(ScorePath, name)],
                    new NullTest(new FieldOperand(ScorePath), false, false), [new(LabelPath, false)],
                    SqlNullOrderingClusterProtocol.RowCount), AllowFullScan: true);
            await EqualAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAstAsync(ast, token)), json);
            await EqualAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, json);
        }
    }

    private static async Task EqualAsync(QueryPage actual, string json)
    {
        var expected = new QueryPage(
            [new("null_a", Revision, json, RedactedFields: []), new("null_b", Revision, json, RedactedFields: [])],
            null, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }

    private const string ExplicitAs = "AS ";
    private const string ScorePath = "/score";
    private const string LabelPath = "/label";
    private const string AccessPath = "bounded-full-scan";
    private const long Revision = 1;
}
