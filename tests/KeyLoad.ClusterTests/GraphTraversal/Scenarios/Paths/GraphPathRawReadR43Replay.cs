using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal static class GraphPathRawReadR43Replay
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathRawReadR43State state, CancellationToken token)
    {
        await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
        await GraphPathRawReadR43Reads.StageAsync(admin, reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.CommitAsync(state.Command, token)));
        await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
        await GraphPathRawReadR43Reads.StageAsync(admin, reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await GraphPathRawReadR43Current.ObserveAsync(admin, state, token);
        await GraphPathRawReadR43Reads.StageAsync(admin, reader, state, token);
    }
}
