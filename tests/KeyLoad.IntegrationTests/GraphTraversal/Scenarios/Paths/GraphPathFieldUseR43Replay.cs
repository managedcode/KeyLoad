using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphPathFieldUseR43Replay
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathFieldUseR43State state, CancellationToken token)
    {
        await GraphPathFieldUseR43Reads.StageAsync(admin, reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.CommitAsync(state.Command, token)));
        await GraphPathFieldUseR43Reads.StageAsync(admin, reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await GraphPathFieldUseR43Reads.StageAsync(admin, reader, state, token);
    }
}
