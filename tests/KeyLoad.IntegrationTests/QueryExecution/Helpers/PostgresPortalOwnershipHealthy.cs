using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipHealthy
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
        {
            await PostgresPortalOwnershipAssertions.PageAsync(state,
                await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, state.Operation, token));
            await PostgresPortalOwnershipAssertions.PageAsync(state,
                await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, state.Operation, token));
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresPortalOwnershipAssertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
                    await PostgresPortalOwnershipAssertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: false, token));
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Writer.Secret, async writer =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(state.Seed, token)));
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
            await DocumentsAsync(writer, state, token);
            return true;
        }, token);
    }

    private static async Task DocumentsAsync(RequestCqrsRf3Callers writer, PostgresRf3State state, CancellationToken token)
    {
        foreach (var reference in new[] { PostgresRf3State.Left, PostgresRf3State.Right })
        {
            var json = reference == PostgresRf3State.Left ? PostgresRf3State.LeftJson : PostgresRf3State.RightJson;
            var expected = new DocumentReadResult(new(reference, PostgresRf3Protocol.SeedRevision, json, false, []));
            await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.GetAsync(reference, state.Receipt.Token, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await writer.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value);
        }
    }
}
