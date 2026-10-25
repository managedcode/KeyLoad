using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Replay
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryCursorRequestIdentityR47State state, CancellationToken token)
    {
        await Assert.That(state.Commands.Length).IsEqualTo(state.Receipts.Length);
        await Assert.That(state.Commands.Length).IsGreaterThan(0);
        for (var index = 0; index < state.Commands.Length; index++)
        {
            await SqlClusterProtocol.EqualAsync(state.Receipts[index], await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CommitAsync(state.Commands[index], token)));
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, state, token);
            _ = await QueryCursorRequestIdentityR47Pages.FreshAsync(root, reader, state, token);
            await SqlClusterProtocol.EqualAsync(state.Receipts[index], (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Commands[index], token))).Value);
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, state, token);
            _ = await QueryCursorRequestIdentityR47Pages.FreshAsync(root, reader, state, token);
        }
    }
}
