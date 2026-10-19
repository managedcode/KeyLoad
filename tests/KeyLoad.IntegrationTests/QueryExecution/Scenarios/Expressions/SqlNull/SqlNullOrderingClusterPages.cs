using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullOrderingClusterPages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            await ProfileAsync(caller, token);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var descending in new[] { false, true })
                    {
                        foreach (var first in new[] { false, true })
                        {
                            await CaseAsync(caller, wire, descending, first, token);
                        }
                    }
                    await SqlAllClusterPages.DuplicatesAsync(caller, wire, token);
                    await SqlImplicitAliasClusterPages.HealthyAsync(caller, wire, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }

    private static async Task ProfileAsync(RequestCqrsClusterCallers caller, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
        var mcp = (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value;
        await global::TUnit.Assertions.Assert.That(sdk.ReadProfiles.Contains(SqlNullOrderingClusterProtocol.Profile)).IsTrue();
        await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullOrderingClusterProtocol.Profile)).IsTrue();
        await global::TUnit.Assertions.Assert.That(sdk.ReadProfiles.Contains(SqlNullOrderingClusterProtocol.UsingProfile)).IsTrue();
        await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullOrderingClusterProtocol.UsingProfile)).IsTrue();
    }

    internal static async Task CaseAsync(RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        bool descending, bool first, CancellationToken token)
    {
        var sql = SqlNullOrderingClusterProtocol.Sql(descending, first);
        await AllAsync(caller, wire, descending, first, token);
        await UsingAsync(caller, wire, descending, first, token);
        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token), descending, first);
        var ast = SqlNullOrderingClusterProtocol.Ast(descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), descending, first);
    }

    private static async Task UsingAsync(RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        bool descending, bool first, CancellationToken token)
    {
        var sql = SqlNullOrderingClusterProtocol.Using(descending, first);
        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), descending, first);
    }

    private static async Task AllAsync(RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        bool descending, bool first, CancellationToken token)
    {
        var sql = SqlAllClusterPages.Explicit(SqlNullOrderingClusterProtocol.Sql(descending, first));
        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), descending, first);
        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), descending, first);
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, SqlNullOrderingClusterState state, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Writer.Secret, async caller =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(state.Seed, token)));
            await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
            return true;
        }, token);
    }
}
