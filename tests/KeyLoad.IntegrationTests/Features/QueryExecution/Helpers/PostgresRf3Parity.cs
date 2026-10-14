using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Parity
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var before = await PostgresRf3Assertions.ImageAsync(root.Sdk, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Reader.Secret, async reader =>
            {
                await PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(state.Query, token)));
                await PageAsync(state, await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, state.Operation, token));
                await PageAsync(state, (await McpCallerAssertions.SuccessAsync<QueryPage>(
                    await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, state.Query, token))).Value);
                await PageAsync(state, await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, state.Operation, token));
                var failures = new List<Exception>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                        state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await PageAsync(state, await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: false, token));
                        await PageAsync(state, await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
                        await SyntaxVariantsAsync(state, wire, token);
                        await LiteralVariantsAsync(state, reader, wire, token);
                        await NamedAndNativeSlotsAsync(state, reader, wire, token);
                        await SqlTableRf3Pages.RequireAsync(state, reader, wire, token);
                        await SqlTableRf3Refusals.RequireAsync(state, reader, wire, root.Sdk, token);
                        await QuotedIdentifiersAsync(state, wire, token);
                        if (state.Dialect == PostgresRf3Protocol.JoinDialect)
                        {
                            var plain = state.Sql.Replace(" INNER JOIN ", " JOIN ", StringComparison.Ordinal);
                            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(plain, [], prepared: false, token));
                            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(plain, [], prepared: true, token));
                        }
                    }, failures);
                }, failures);
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root.Sdk, token)).IsEqualTo(before);
            return true;
        }, token);
    }

    private static Task PageAsync(PostgresRf3State state, QueryPage page)
        => PostgresRf3Assertions.PageAsync(state, page);

    private static async Task LiteralVariantsAsync(PostgresRf3State state,
        RequestCqrsRf3Callers reader, PostgresRf3Caller wire, CancellationToken token)
    {
        foreach (var predicate in new[] { "10e-1 = 1", "1 = 1", "-1 < 0",
            "79228162514264337593543950335 = 79228162514264337593543950335",
            "-79228162514264337593543950335 = -79228162514264337593543950335",
            "N'😀' = N'😀'", "N'guard' = N'guard'", "N'$9' = N'$9' /* $9 */" })
        {
            var sql = state.Sql.Replace(" ORDER BY ", " WHERE " + predicate + " ORDER BY ", StringComparison.Ordinal);
            await global::TUnit.Assertions.Assert.That(sql).IsNotEqualTo(state.Sql);
            var operation = state.Operation with
            {
                Sql = sql
            };
            await PageAsync(state, await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, operation, token));
            await PageAsync(state, await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, operation, token));
            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token));
            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token));
        }
    }

    private static async Task SyntaxVariantsAsync(PostgresRf3State state, PostgresRf3Caller wire, CancellationToken token)
    {
        var all = state.Sql.Replace("SELECT ", "SELECT ALL ", StringComparison.Ordinal);
        foreach (var sql in new[] { all, state.Sql.Replace(" AS ", " ", StringComparison.Ordinal),
            all.Replace(" AS ", " ", StringComparison.Ordinal) })
        {
            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token));
            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token));
        }
    }
    private static async Task NamedAndNativeSlotsAsync(PostgresRf3State state,
        RequestCqrsRf3Callers reader, PostgresRf3Caller wire, CancellationToken token)
    {
        foreach (var named in new[] { " WHERE 1 = @p1 ORDER BY ", " WHERE @p1 = @p1 ORDER BY " })
        {
            var operation = state.Operation with
            {
                Sql = state.Sql.Replace(" ORDER BY ", named, StringComparison.Ordinal),
                Parameters = new(StringComparer.Ordinal)
                {
                    [ParameterName] = global::System.Text.Json.JsonSerializer.SerializeToElement(ParameterValue, JsonDefaults.Options)
                }
            };
            await PageAsync(state, await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, operation, token));
            await PageAsync(state, await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, operation, token));
            var predicate = named == " WHERE 1 = @p1 ORDER BY "
                ? " WHERE 1 = $1 ORDER BY " : " WHERE $1 = $1 ORDER BY ";
            var sql = state.Sql.Replace(" ORDER BY ", predicate, StringComparison.Ordinal);
            foreach (var prepared in new[] { false, true })
            {
                var parameter = new global::Npgsql.NpgsqlParameter
                {
                    NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Integer,
                    Value = ParameterValue
                };
                await PageAsync(state, await wire.ExecuteAsync<QueryPage>(sql, [parameter], prepared, token));
            }
        }
    }

    private static async Task QuotedIdentifiersAsync(PostgresRf3State state,
        PostgresRf3Caller wire, CancellationToken token)
    {
        var sql = state.Sql.Replace("budgetorders", "[budgetorders]", StringComparison.Ordinal)
            .Replace("budgetcustomers", "[budgetcustomers]", StringComparison.Ordinal);
        foreach (var prepared in new[] { false, true })
        {
            await PageAsync(state, await wire.ExecuteAsync<QueryPage>(sql, [], prepared, token));
        }
    }

    private const string ParameterName = "p1";
    private const int ParameterValue = 1;

}
