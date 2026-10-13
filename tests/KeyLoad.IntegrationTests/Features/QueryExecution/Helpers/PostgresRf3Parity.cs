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
}
