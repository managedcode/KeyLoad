using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountClusterPages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlFetchCountClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlFetchCountClusterAssertions.ImageAsync(root, state, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            await ProfileAsync(caller, token);
            await SqlFetchCountClusterMinimumToken.RequireAsync(caller, state, token);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var item in SqlFetchCountClusterProtocol.Cases)
                    {
                        await CaseAsync(caller, wire, item.Sql, item.Count, token);
                    }
                    await WindowAsync(caller, wire, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }

    private const int WindowCount = 2;
    private const int WindowOffset = 1;
    private const string CombinedWindow = "SELECT label AS label FROM fetch_count ORDER BY label ASC OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY";
    private static async Task WindowAsync(RequestCqrsClusterCallers caller, PostgresClusterCaller wire, CancellationToken token)
    {
        var operation = SqlFetchCountClusterProtocol.Operation(CombinedWindow);
        var ast = SqlFetchCountClusterProtocol.Ast(WindowCount);
        ast = ast with
        {
            Query = ast.Query with
            {
                Offset = WindowOffset
            }
        };
        var pages = new[]
        {
            await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token),
            await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token),
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)),
            (await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            await wire.ExecuteAsync<QueryPage>(CombinedWindow, [], prepared: true, token),
            await wire.ExecuteAsync<QueryPage>(CombinedWindow, [], prepared: false, token)
        };
        foreach (var page in pages)
        {
            var expected = new QueryPage(
                [new("b", SqlFetchCountClusterProtocol.SeedRevision, "{\"label\":\"b\"}", RedactedFields: []), new("c", SqlFetchCountClusterProtocol.SeedRevision, "{\"label\":\"c\"}", RedactedFields: [])],
                null, page.CutPosition, "bounded-full-scan");
            await SqlClusterProtocol.EqualAsync(expected, page);
        }
    }

    private static async Task ProfileAsync(RequestCqrsClusterCallers caller, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
        var mcp = (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value;
        await global::TUnit.Assertions.Assert.That(sdk.ReadProfiles.Contains(SqlFetchCountClusterProtocol.Profile)).IsTrue();
        await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlFetchCountClusterProtocol.Profile)).IsTrue();
    }

    internal static async Task CaseAsync(RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        string sql, int count, CancellationToken token)
    {
        var operation = SqlFetchCountClusterProtocol.Operation(sql);
        await SqlFetchCountClusterAssertions.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), count);
        await SqlFetchCountClusterAssertions.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, operation, token), count);
        var ast = SqlFetchCountClusterProtocol.Ast(count);
        await SqlFetchCountClusterAssertions.PageAsync(await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), count);
        await SqlFetchCountClusterAssertions.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, count);
        await SqlFetchCountClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), count);
        await SqlFetchCountClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), count);
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, SqlFetchCountClusterState state, CancellationToken token)
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
