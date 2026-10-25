using KeyLoad.Query;
using Npgsql;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlImplicitAliasClusterPages
{
    internal const string BasicSql = "SELECT TOP (5) score projected_score FROM null_ordering WHERE score IS NULL ORDER BY label ASC";
    private const string Prefix = "SELECT TOP (5) score ";
    private const string Tail = " FROM null_ordering WHERE score IS NULL ORDER BY label ASC";
    private static readonly (Func<NpgsqlCommand> Implicit, Func<NpgsqlCommand> Explicit, string Name, string Json)[] Cases =
    [
        (() => new(Prefix + "projected_score" + Tail), () => new(Prefix + ExplicitAs + "projected_score" + Tail), "projected_score", "{\"projected_score\":null}"),
        (() => new(Prefix + "\"ORDER\"" + Tail), () => new(Prefix + ExplicitAs + "\"ORDER\"" + Tail), "ORDER", "{\"ORDER\":null}"),
        (() => new(Prefix + "\"FROM\"" + Tail), () => new(Prefix + ExplicitAs + "\"FROM\"" + Tail), "FROM", "{\"FROM\":null}")
    ];

    internal static async Task HealthyAsync(global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire, CancellationToken token)
    {
        foreach (var (implicitCommand, explicitCommand, name, json) in Cases)
        {
            foreach (var command in new[] { implicitCommand, explicitCommand })
            {
                var operation = SqlNullOrderingClusterProtocol.Operation(PostgresClusterCommands.Sql(command));
                await EqualAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), json);
                await EqualAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token), json);
                await EqualAsync(await wire.ExecuteAsync<QueryPage>(command, [], prepared: true, token), json);
                await EqualAsync(await wire.ExecuteAsync<QueryPage>(command, [], prepared: false, token), json);
            }
            var ast = new AstQueryRequest(PostgresClusterState.Partition,
                new(SqlNullOrderingClusterProtocol.Collection, null, [new(ScorePath, name)],
                    new NullTest(new FieldOperand(ScorePath), false, false), [new(LabelPath, false)],
                    SqlNullOrderingClusterProtocol.RowCount), AllowFullScan: true);
            await EqualAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.QueryAstAsync(ast, token)), json);
            await EqualAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
                await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, json);
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
