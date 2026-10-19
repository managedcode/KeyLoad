using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRawReadR43Current
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin, GraphPathRawReadR43State state,
        CancellationToken token)
    {
        foreach (var vertex in state.Vertices)
        {
            var expected = new DocumentResult(vertex, 1L, "{}", false, []);
            await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(vertex, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(vertex), token))).Value);
        }
        var request = new TraverseRequest(state.Seed.Partition, GraphPathClusterScenario.Graph, state.Source, 2, 3, 2);
        var expectedGraph = new global::KeyLoad.GraphTraversal([state.Middle, state.Source, state.Target], [.. state.Edges]);
        await SqlClusterProtocol.EqualAsync(expectedGraph, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.TraverseAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expectedGraph, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await admin.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(admin, state, token));
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsClusterCallers admin,
        GraphPathRawReadR43State state, CancellationToken token)
    {
        var current = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Seed.Partition, token));
        await SqlClusterProtocol.EqualAsync(current, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Seed.Partition), token))).Value);
        return current;
    }
}
