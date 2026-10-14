using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Pages
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlCreateTableRf3State state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var request = new SqlOperationRequest(SqlCreateTableRf3Literal.Partition, SqlCreateTableRf3Literal.ReadSql, AllowFullScan: true);
            var image = await SqlCreateTableRf3Images.ReadAsync(caller.Sdk, token);
            if (state.RepairId is { } repairId)
            {
                var repair = SqlCreateTableRf3Literal.Request(repairId, SqlCreateTableRf3Literal.RepairSql);
                var expected = SqlCreateTableRf3Literal.Definition(SqlCreateTableRf3Literal.RepairTable);
                await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(caller.Sdk, repair, token));
                await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<ResourceDefinition>(caller.Mcp, repair, token));
            }
            await PageAsync(state, await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, request, token));
            var ast = new global::KeyLoad.Query.AstQueryRequest(SqlCreateTableRf3Literal.Partition,
                new global::KeyLoad.Query.SelectQuery(SqlCreateTableRf3Literal.Table, null,
                    [new("/title", "title"), new("/amount", "amount"), new("/active", "active")],
                    null, [new("/@id", false)], MaximumRows), AllowFullScan: true);
            await PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)));
            await SqlRf3Protocol.EqualAsync(image.Document, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(
                new(SqlCreateTableRf3Literal.Partition, SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One), state.Receipt.Token, token)));
            await PageAsync(state, await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, request, token));
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                { await PageAsync(state, await wire.ExecuteAsync<QueryPage>(SqlCreateTableRf3Literal.ReadSql, [], prepared: false, token)); }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            await SqlCreateTableRf3Images.RequireAsync(caller.Sdk, image, token);
            return true;
        }, token);
    private static async Task PageAsync(SqlCreateTableRf3State state, QueryPage page)
    {
        await SqlRf3Protocol.EqualAsync(new QueryRow[] { new(SqlCreateTableRf3Literal.One, state.Revision,
            state.Selected, false, []) }, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(SqlCreateTableRf3Literal.AccessPath);
        // This public cut is opaque: no equality or ordering against RF3 receipt position is claimed.
    }
    private const int MaximumRows = 5;
}
