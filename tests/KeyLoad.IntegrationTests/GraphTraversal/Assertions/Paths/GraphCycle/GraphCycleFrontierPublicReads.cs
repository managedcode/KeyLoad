using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphCycleFrontierPublicReads
{
    internal static async Task MatrixAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphCycleFrontierPublicState state, CancellationToken token)
    {
        for (var depth = 1; depth <= 3; depth++)
        {
            var vertices = VertexLimit(depth, state.Hidden);
            var edges = EdgeLimit(depth, state.Hidden);
            var request = state.Request(depth, vertices, edges);
            await SqlClusterProtocol.EqualAsync(state.Expected(depth, state.Hidden), await McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.TraverseAsync(request, token)));
            await GraphCycleFrontierPublicCurrent.ObserveAsync(admin, state, token);
            var reply = await reader.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token);
            await SqlClusterProtocol.EqualAsync(state.Expected(depth, state.Hidden),
                (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(reply)).Value);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Seed.Reader.Secret, GraphIncomingClusterScenario.PrivateMarker);
            await GraphCycleFrontierPublicCurrent.ObserveAsync(admin, state, token);
        }
        foreach (var vertex in state.Vertices.Where(vertex => !state.Hidden || vertex != state.A))
        {
            await IncomingAsync(admin, reader, state, vertex, token);
        }
    }

    private static int VertexLimit(int depth, bool hidden)
    {
        if (depth == 1)
        {
            return hidden ? 2 : 3;
        }
        return hidden ? 3 : 4;
    }

    private static int EdgeLimit(int depth, bool hidden)
    {
        if (depth == 1)
        {
            return 3;
        }
        if (depth == 2)
        {
            return hidden ? 4 : 6;
        }
        return hidden ? 5 : 7;
    }

    private static async Task IncomingAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphCycleFrontierPublicState state, EntityRef vertex, CancellationToken token)
    {
        var request = new ReadIncomingGraphEdgesRequest(1, vertex, state.Seed.Graph, 7);
        var expected = state.Edges.Where(edge => edge.To == vertex && (!state.Hidden || edge.From != state.A))
            .OrderBy(edge => edge.From.Collection, StringComparer.Ordinal).ThenBy(edge => edge.From.Id, StringComparer.Ordinal)
            .ThenBy(edge => edge.Id, StringComparer.Ordinal).Select(edge => new GraphIncomingEdgeRow(state.Seed.StoredEdge(edge), 0L)).ToArray();
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.GraphIncomingEdgesClient.IncomingEdgesAsync(
            reader.Sdk, request, token));
        await PageAsync(sdk, expected);
        await GraphCycleFrontierPublicCurrent.ObserveAsync(admin, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.GraphIncomingEdges, request, token);
        await PageAsync((await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(reply)).Value, expected);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Seed.Reader.Secret, GraphIncomingClusterScenario.PrivateMarker);
        await GraphCycleFrontierPublicCurrent.ObserveAsync(admin, state, token);
    }

    private static async Task PageAsync(GraphIncomingEdgesPage page, GraphIncomingEdgeRow[] rows)
    {
        await SqlClusterProtocol.EqualAsync(rows, page.Rows.ToArray());
        await Assert.That(page.Version).IsEqualTo(1);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.Projection).IsEqualTo(GraphIncomingClusterScenario.Projection);
    }
}
