using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalRf3Pages
{
    internal static async Task PageAsync(QueryPage actual, decimal threshold)
    {
        global::System.Collections.Immutable.ImmutableArray<QueryRow> rows = threshold <= decimal.One
            ? [Row(PostgresDecimalRf3Protocol.HighId, PostgresDecimalRf3Protocol.HighJson),
               Row(PostgresDecimalRf3Protocol.LowId, PostgresDecimalRf3Protocol.LowJson)]
            : threshold <= PostgresDecimalRf3Protocol.HighScore
                ? [Row(PostgresDecimalRf3Protocol.HighId, PostgresDecimalRf3Protocol.HighJson)] : [];
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
                    foreach (var value in PostgresDecimalRf3Protocol.Thresholds)
                    {
                        var request = PostgresDecimalRf3Protocol.Operation(value);
                        await PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, request, token), value);
                        await PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, request, token), value);
                        var ast = PostgresDecimalRf3Protocol.Ast(value);
                        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), value);
                        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
                            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, value);
                        foreach (var prepared in new[] { false, true })
                        {
                            var parameter = new global::Npgsql.NpgsqlParameter(PostgresDecimalRf3Protocol.Parameter,
                                global::NpgsqlTypes.NpgsqlDbType.Numeric)
                            { Value = value };
                            await PageAsync(await wire.ExecuteAsync<QueryPage>(PostgresDecimalRf3Protocol.DriverSql,
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
