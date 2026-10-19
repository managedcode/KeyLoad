using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRebindR44Current
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin, GraphPathRebindR44State state,
        CancellationToken token)
    {
        foreach (var vertex in state.Vertices)
        {
            var document = new DocumentResult(vertex, 1L, "{}", false, []);
            await SqlClusterProtocol.EqualAsync(document, await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(vertex, token)));
            await SqlClusterProtocol.EqualAsync(document, (await PublicReadAssertions.DocumentAsync(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(vertex), token))).Value);
            var request = new TraverseRequest(state.Seed.Partition, GraphPathClusterScenario.Graph, vertex, 1, 4, 4);
            await SqlClusterProtocol.EqualAsync(state.Outgoing(vertex), await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.TraverseAsync(request, token)));
            await SqlClusterProtocol.EqualAsync(state.Outgoing(vertex), (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
                await admin.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
            await IncomingAsync(admin, state, vertex, token);
        }
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(admin, state, token));
    }

    private static async Task IncomingAsync(RequestCqrsClusterCallers admin, GraphPathRebindR44State state,
        EntityRef vertex, CancellationToken token)
    {
        var request = new ReadIncomingGraphEdgesRequest(1, vertex, GraphPathClusterScenario.Graph, 4);
        var rows = state.Edges.Where(edge => edge.To == vertex).OrderBy(edge => edge.From.Collection, StringComparer.Ordinal)
            .ThenBy(edge => edge.From.Id, StringComparer.Ordinal).ThenBy(edge => edge.Id, StringComparer.Ordinal)
            .Select(edge => new GraphIncomingEdgeRow(edge, 0L)).ToArray();
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.GraphIncomingEdgesClient.IncomingEdgesAsync(
            admin.Sdk, request, token)), rows);
        await PageAsync((await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(await admin.Mcp.CallAsync(
            McpCallerTools.GraphIncomingEdges, request, token))).Value, rows);
    }

    private static async Task PageAsync(GraphIncomingEdgesPage page, GraphIncomingEdgeRow[] rows)
    {
        await SqlClusterProtocol.EqualAsync(rows, page.Rows.ToArray());
        await Assert.That(page.Version).IsEqualTo(1);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.Projection).IsEqualTo(GraphIncomingClusterScenario.Projection);
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsClusterCallers admin,
        GraphPathRebindR44State state, CancellationToken token)
    {
        var current = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Seed.Partition, token));
        await SqlClusterProtocol.EqualAsync(current, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Seed.Partition), token))).Value);
        return current;
    }
}
