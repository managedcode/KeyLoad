using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Refusals
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryCursorRequestIdentityR47State state,
        bool afterCold, CancellationToken token)
    {
        foreach (var original in state.OriginalPages)
        {
            var request = state.ChangedRequest with
            {
                Cursor = original.Cursor
            };
            var sdk = await reader.Sdk.QueryAsync(request, token);
            await Assert.That(sdk.IsSuccess).IsFalse();
            await Assert.That(sdk.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.CursorExpired,
                QueryCursorRequestIdentityR47Protocol.CursorDetail), sdk.Problem);
            await PrivateAsync(JsonSerializer.Serialize(sdk), state.Credential);
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, state, token);
            if (!afterCold)
            {
                await QueryCursorRequestIdentityR47Pages.ContinueAsync(root, reader, state, original, 0, token);
            }
            var reply = await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token);
            _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.CursorExpired, dispatched: true)).Value;
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(QueryCursorRequestIdentityR47Protocol.DetailKey).GetString())
                .IsEqualTo(QueryCursorRequestIdentityR47Protocol.SafeCursorDetail);
            await PrivateAsync(JsonSerializer.Serialize(reply), state.Credential);
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, state, token);
            if (!afterCold)
            {
                await QueryCursorRequestIdentityR47Pages.ContinueAsync(root, reader, state, original, 1, token);
            }
        }
    }

    private static async Task PrivateAsync(string serialized, string credential)
        => await Assert.That(serialized.Contains(credential, StringComparison.Ordinal)).IsFalse();
}
