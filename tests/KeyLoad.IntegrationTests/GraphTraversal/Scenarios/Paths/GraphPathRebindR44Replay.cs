using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRebindR44Replay
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathRebindR44State state, CancellationToken token)
    {
        await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
        foreach (var original in state.Originals)
        {
            await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(original.Command, token)));
            await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
            await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
            await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
        }
        if (state.Stage > 1)
        {
            await GraphPathRebindR44Refusals.StaleAsync(admin, reader, state, token);
        }
        await GraphPathRebindR44Refusals.BoundsAsync(admin, reader, state, token);
    }
}
