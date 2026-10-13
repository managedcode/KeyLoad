using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlAllRf3Pages
{
    internal const string Keyword = "SELECT ALL ";
    internal const string DefaultKeyword = "SELECT ";
    internal const string DuplicateSql = "SELECT ALL score AS score FROM null_ordering WHERE score IS NULL ORDER BY label ASC LIMIT 5";
    internal static string Explicit(string sql) => Keyword + sql[DefaultKeyword.Length..];

    internal static async Task DuplicatesAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire, CancellationToken token)
    {
        foreach (var sql in new[] { DuplicateSql, DuplicateSql.Replace(Keyword, DefaultKeyword, StringComparison.Ordinal) })
        {
            var operation = SqlNullOrderingRf3Protocol.Operation(sql);
            await EqualAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, operation, token));
            await EqualAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, operation, token));
            await EqualAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token));
            await EqualAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token));
        }
        var ast = new AstQueryRequest(PostgresRf3State.Partition,
            new(SqlNullOrderingRf3Protocol.Collection, null, [new(ScorePath, ScoreName)],
                new NullTest(new FieldOperand(ScorePath), false, false), [new(LabelPath, false)],
                SqlNullOrderingRf3Protocol.RowCount), AllowFullScan: true);
        await EqualAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)));
        await EqualAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value);
    }

    private static async Task EqualAsync(QueryPage actual)
    {
        var expected = new QueryPage(
            [new("null_a", Revision, "{\"score\":null}", RedactedFields: []),
             new("null_b", Revision, "{\"score\":null}", RedactedFields: [])],
            null, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    private const string ScorePath = "/score";
    private const string LabelPath = "/label";
    private const string ScoreName = "score";
    private const string AccessPath = "bounded-full-scan";
    private const long Revision = 1;
}
