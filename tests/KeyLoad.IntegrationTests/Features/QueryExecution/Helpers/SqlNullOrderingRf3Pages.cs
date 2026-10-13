using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullOrderingRf3Pages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlNullOrderingRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            await ProfileAsync(caller, token);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var descending in new[] { false, true })
                    {
                        foreach (var first in new[] { false, true })
                        { await CaseAsync(caller, wire, descending, first, token); }
                    }
                    await SqlAllRf3Pages.DuplicatesAsync(caller, wire, token);
                    await SqlImplicitAliasRf3Pages.HealthyAsync(caller, wire, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }

    private static async Task ProfileAsync(RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
        var mcp = (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new { }, token))).Value;
        await global::TUnit.Assertions.Assert.That(sdk.ReadProfiles.Contains(SqlNullOrderingRf3Protocol.Profile)).IsTrue();
        await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullOrderingRf3Protocol.Profile)).IsTrue();
    }

    internal static async Task CaseAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        bool descending, bool first, CancellationToken token)
    {
        var sql = SqlNullOrderingRf3Protocol.Sql(descending, first);
        await AllAsync(caller, wire, descending, first, token);
        var operation = SqlNullOrderingRf3Protocol.Operation(sql);
        await SqlNullOrderingRf3Assertions.PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, operation, token), descending, first);
        var ast = SqlNullOrderingRf3Protocol.Ast(descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), descending, first);
    }

    private static async Task AllAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        bool descending, bool first, CancellationToken token)
    {
        var sql = SqlAllRf3Pages.Explicit(SqlNullOrderingRf3Protocol.Sql(descending, first));
        var operation = SqlNullOrderingRf3Protocol.Operation(sql);
        await SqlNullOrderingRf3Assertions.PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, operation, token), descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), descending, first);
        await SqlNullOrderingRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), descending, first);
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, SqlNullOrderingRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Writer.Secret, async caller =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(state.Seed, token)));
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
            return true;
        }, token);
    }
}
