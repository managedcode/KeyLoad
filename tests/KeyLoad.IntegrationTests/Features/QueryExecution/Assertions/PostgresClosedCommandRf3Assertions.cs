using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClosedCommandRf3Assertions
{
    internal static async Task ModelsAsync(KeyLoadClient root, PostgresClosedCommandRf3State state, CancellationToken token)
    {
        await PostgresCompositionRf3Models.LiteralAsync(state.Image, reverse: true);
        await SqlRf3Protocol.EqualAsync(state.Image, await PostgresCompositionRf3Models.ReadAsync(root, token));
    }

    internal static Task TargetAsync(ClusterFixture fixture, PostgresClosedCommandRf3State state,
        bool allowed, Func<Task> afterRead, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(afterRead);
        return RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Target.Secret, async target =>
        {
            foreach (var reference in new[] { PostgresRf3State.Left, PostgresRf3State.Right })
            {
                var request = new GetDocumentRequest(reference);
                var expected = reference == PostgresRf3State.Left ? state.Image.Left : state.Image.Right;
                var sdk = await target.Sdk.GetAsync(reference, token);
                if (allowed)
                { await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(sdk)); }
                else
                {
                    await Assert.That(sdk.IsFailed).IsTrue();
                    await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
                }
                await afterRead();
                var official = await target.Mcp.CallAsync(McpCallerTools.DocumentsGet, request, token);
                if (allowed)
                { await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(official)).Value); }
                else
                { await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true); }
                await afterRead();
            }
            return true;
        }, token);
    }

    internal static async Task NativeHealthyAsync(PostgresRf3Caller wire,
        PostgresClosedCommandRf3State state, bool prepared, CancellationToken token)
    {
        foreach (var reference in new[] { PostgresRf3State.Left, PostgresRf3State.Right })
        {
            var expected = reference == PostgresRf3State.Left ? state.Image.Left : state.Image.Right;
            await SqlRf3Protocol.EqualAsync(new DocumentReadResult(expected),
                await wire.ExecuteAsync<DocumentReadResult>(PostgresPureReadRf3Protocol.DocumentSql,
                    PostgresPureReadRf3Protocol.Parameters(new GetDocumentRequest(reference)), prepared, token));
        }
    }
}
