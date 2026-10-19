using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalClusterPages
{
    internal static async Task PageAsync(QueryPage actual, decimal threshold)
    {
        global::System.Collections.Immutable.ImmutableArray<QueryRow> rows = threshold <= decimal.One
            ? [Row(PostgresDecimalClusterProtocol.HighId, PostgresDecimalClusterProtocol.HighJson),
               Row(PostgresDecimalClusterProtocol.LowId, PostgresDecimalClusterProtocol.LowJson)]
            : threshold <= PostgresDecimalClusterProtocol.HighScore
                ? [Row(PostgresDecimalClusterProtocol.HighId, PostgresDecimalClusterProtocol.HighJson)] : [];
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
                    foreach (var value in PostgresDecimalClusterProtocol.Thresholds)
                    {
                        var request = PostgresDecimalClusterProtocol.Operation(value);
                        await PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, request, token), value);
                        await PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, request, token), value);
                        var ast = PostgresDecimalClusterProtocol.Ast(value);
                        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), value);
                        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
                            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, value);
                        foreach (var prepared in new[] { false, true })
                        {
                            var parameter = new global::Npgsql.NpgsqlParameter(PostgresDecimalClusterProtocol.Parameter,
                                global::NpgsqlTypes.NpgsqlDbType.Numeric)
                            {
                                Value = value
                            };
                            await PageAsync(await wire.ExecuteAsync<QueryPage>(PostgresDecimalClusterProtocol.DriverSql,
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
