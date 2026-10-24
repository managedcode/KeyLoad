using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphLabelFrontierPublicCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin, GraphLabelFrontierPublicState state,
        CancellationToken token)
    {
        foreach (var vertex in state.Vertices)
        {
            var expected = new DocumentResult(vertex, 1L, "{}", false, []);
            await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(vertex, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(vertex), token))).Value);
        }
        var request = state.Request(3, 0, 4, 5);
        await SqlClusterProtocol.EqualAsync(state.Expected(3, 0), await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.TraverseAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(state.Expected(3, 0), (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await admin.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(admin, state, token));
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsClusterCallers admin,
        GraphLabelFrontierPublicState state, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Seed.TargetPartition, token));
        await SqlClusterProtocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Seed.TargetPartition), token))).Value);
        return result;
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphLabelFrontierPublicState state, CancellationToken token)
    {
        foreach (var original in state.Originals)
        {
            await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(original.Command, token)));
            await GraphLabelFrontierPublicReads.MatrixAsync(admin, reader, state, token);
            await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
            await GraphLabelFrontierPublicReads.MatrixAsync(admin, reader, state, token);
        }
    }
}
