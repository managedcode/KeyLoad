using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountRf3Pages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlFetchCountRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlFetchCountRf3Assertions.ImageAsync(root, state, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            await ProfileAsync(caller, token);
            await SqlFetchCountRf3MinimumToken.RequireAsync(caller, state, token);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var item in SqlFetchCountRf3Protocol.Cases)
                    {
                        await CaseAsync(caller, wire, item.Sql, item.Count, token);
                    }
                    await WindowAsync(caller, wire, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }

    private const int WindowCount = 2;
    private const int WindowOffset = 1;
    private const string CombinedWindow = "SELECT label AS label FROM fetch_count ORDER BY label ASC OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY";
    private static async Task WindowAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire, CancellationToken token)
    {
        var operation = SqlFetchCountRf3Protocol.Operation(CombinedWindow);
        var ast = SqlFetchCountRf3Protocol.Ast(WindowCount);
        ast = ast with
        {
            Query = ast.Query with
            {
                Offset = WindowOffset
            }
        };
        var pages = new[]
        {
            await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, operation, token),
            await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, operation, token),
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)),
            (await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            await wire.ExecuteAsync<QueryPage>(CombinedWindow, [], prepared: true, token),
            await wire.ExecuteAsync<QueryPage>(CombinedWindow, [], prepared: false, token)
        };
        foreach (var page in pages)
        {
            var expected = new QueryPage(
                [new("b", SqlFetchCountRf3Protocol.SeedRevision, "{\"label\":\"b\"}", RedactedFields: []), new("c", SqlFetchCountRf3Protocol.SeedRevision, "{\"label\":\"c\"}", RedactedFields: [])],
                null, page.CutPosition, "bounded-full-scan");
            await SqlRf3Protocol.EqualAsync(expected, page);
        }
    }

    private static async Task ProfileAsync(RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
        var mcp = (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value;
        await global::TUnit.Assertions.Assert.That(sdk.ReadProfiles.Contains(SqlFetchCountRf3Protocol.Profile)).IsTrue();
        await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlFetchCountRf3Protocol.Profile)).IsTrue();
    }

    internal static async Task CaseAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        string sql, int count, CancellationToken token)
    {
        var operation = SqlFetchCountRf3Protocol.Operation(sql);
        await SqlFetchCountRf3Assertions.PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, operation, token), count);
        await SqlFetchCountRf3Assertions.PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, operation, token), count);
        var ast = SqlFetchCountRf3Protocol.Ast(count);
        await SqlFetchCountRf3Assertions.PageAsync(await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.QueryAstAsync(ast, token)), count);
        await SqlFetchCountRf3Assertions.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, count);
        await SqlFetchCountRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), count);
        await SqlFetchCountRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), count);
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, SqlFetchCountRf3State state, CancellationToken token)
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
