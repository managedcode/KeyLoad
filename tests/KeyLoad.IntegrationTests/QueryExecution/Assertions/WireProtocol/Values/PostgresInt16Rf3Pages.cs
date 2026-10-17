using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16Rf3Pages
{
    internal static async Task PageAsync(QueryPage actual, short threshold)
    {
        global::System.Collections.Immutable.ImmutableArray<QueryRow> rows = threshold <= PostgresInt16Rf3Protocol.OneValue
            ? [Row(PostgresInt16Rf3Protocol.HighId, PostgresInt16Rf3Protocol.HighJson),
               Row(PostgresInt16Rf3Protocol.LowId, PostgresInt16Rf3Protocol.LowJson)]
            : threshold <= PostgresInt16Rf3Protocol.HighScore
                ? [Row(PostgresInt16Rf3Protocol.HighId, PostgresInt16Rf3Protocol.HighJson)] : [];
        var expected = new QueryPage(rows, null, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    internal static async Task HealthyAsync(ClusterFixture fixture, SqlNullOrderingRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var value in PostgresInt16Rf3Protocol.Thresholds)
                    {
                        var request = PostgresInt16Rf3Protocol.Operation(value);
                        await PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, request, token), value);
                        await PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, request, token), value);
                        var ast = PostgresInt16Rf3Protocol.Ast(value);
                        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), value);
                        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
                            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, value);
                        foreach (var prepared in new[] { false, true })
                        {
                            var parameter = new global::Npgsql.NpgsqlParameter(PostgresInt16Rf3Protocol.Parameter,
                                global::NpgsqlTypes.NpgsqlDbType.Smallint)
                            {
                                Value = value
                            };
                            await PageAsync(await wire.ExecuteAsync<QueryPage>(PostgresInt16Rf3Protocol.DriverSql,
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
        => new(id, SqlNullOrderingRf3Protocol.SeedRevision, json, RedactedFields: []);
    private const string AccessPath = "bounded-full-scan";
}
