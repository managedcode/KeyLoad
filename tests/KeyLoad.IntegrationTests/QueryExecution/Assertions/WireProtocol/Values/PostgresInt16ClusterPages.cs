using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16ClusterPages
{
    internal static async Task PageAsync(QueryPage actual, short threshold)
    {
        global::System.Collections.Immutable.ImmutableArray<QueryRow> rows = threshold <= PostgresInt16ClusterProtocol.OneValue
            ? [Row(PostgresInt16ClusterProtocol.HighId, PostgresInt16ClusterProtocol.HighJson),
               Row(PostgresInt16ClusterProtocol.LowId, PostgresInt16ClusterProtocol.LowJson)]
            : threshold <= PostgresInt16ClusterProtocol.HighScore
                ? [Row(PostgresInt16ClusterProtocol.HighId, PostgresInt16ClusterProtocol.HighJson)] : [];
        var expected = new QueryPage(rows, null, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }

    internal static async Task HealthyAsync(ClusterFixture fixture, SqlNullOrderingClusterState state, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var value in PostgresInt16ClusterProtocol.Thresholds)
                    {
                        var request = PostgresInt16ClusterProtocol.Operation(value);
                        await PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, request, token), value);
                        await PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, request, token), value);
                        var ast = PostgresInt16ClusterProtocol.Ast(value);
                        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), value);
                        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
                            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, value);
                        foreach (var prepared in new[] { false, true })
                        {
                            var parameter = new global::Npgsql.NpgsqlParameter(PostgresInt16ClusterProtocol.Parameter,
                                global::NpgsqlTypes.NpgsqlDbType.Smallint)
                            {
                                Value = value
                            };
                            await PageAsync(await wire.ExecuteAsync<QueryPage>(PostgresInt16ClusterProtocol.DriverSql,
                                [parameter], prepared, token), value);
                        }
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    private static QueryRow Row(string id, string json)
        => new(id, SqlNullOrderingClusterProtocol.SeedRevision, json, RedactedFields: []);
    private const string AccessPath = "bounded-full-scan";
}
