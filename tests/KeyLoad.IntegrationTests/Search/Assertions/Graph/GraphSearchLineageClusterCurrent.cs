using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Search;

internal static class GraphSearchLineageClusterCurrent
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, GraphSearchLineageClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var put in state.Seed.Mutations.OfType<PutDocument>())
        {
            var reference = new EntityRef(state.Partition, GraphSearchLineageClusterProtocol.Collection, put.Id);
            var expected = new DocumentResult(state.StoredVertex(put.Id), 1L, put.Json, false, []);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlClusterProtocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)),
                    new DocumentReadResult(expected));
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlClusterProtocol.EqualAsync((await PublicReadAssertions.DocumentAsync(await root.Mcp.CallAsync(
                    McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value, expected);
            }, failures);
        }
        foreach (var graph in new[] { GraphSearchLineageClusterProtocol.EarlyGraph, GraphSearchLineageClusterProtocol.LateGraph })
        {
            var request = new TraverseRequest(state.Partition, graph,
                GraphSearchLineageClusterRecipe.Vertex(state.Partition, GraphSearchLineageClusterProtocol.Alpha), 2,
                GraphSearchLineageClusterProtocol.Vertices, GraphSearchLineageClusterProtocol.Edges);
            var edges = state.Seed.Mutations.OfType<UpsertEdge>().Where(edge => edge.Graph == graph)
                .Select(edge => new EdgeRecord(edge.EdgeId, state.StoredVertex(edge.From.Id), state.StoredVertex(edge.To.Id),
                    edge.Label, edge.AttributesJson, 1L)).ToArray();
            var vertices = graph == GraphSearchLineageClusterProtocol.EarlyGraph
                ? new[] { GraphSearchLineageClusterProtocol.Alpha, GraphSearchLineageClusterProtocol.Beta }
                : new[] { GraphSearchLineageClusterProtocol.Alpha, GraphSearchLineageClusterProtocol.Beta, GraphSearchLineageClusterProtocol.Context };
            var expected = new global::KeyLoad.GraphTraversal([.. vertices.Select(state.StoredVertex)], [.. edges]);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlClusterProtocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.TraverseAsync(request, token)), expected);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlClusterProtocol.EqualAsync((await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await root.Mcp.CallAsync(
                    McpCallerTools.GraphTraverse, request, token))).Value, expected);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token)), state.Outbox);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(await root.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value, state.Outbox);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
