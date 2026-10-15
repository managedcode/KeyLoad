using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageRf3Current
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, GraphSearchLineageRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var put in state.Seed.Mutations.OfType<PutDocument>())
        {
            var reference = new EntityRef(state.Partition, GraphSearchLineageRf3Protocol.Collection, put.Id);
            var expected = new DocumentResult(reference, 1L, put.Json, false, []);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlRf3Protocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)),
                    new DocumentReadResult(expected));
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlRf3Protocol.EqualAsync((await PublicReadAssertions.DocumentAsync(await root.Mcp.CallAsync(
                    McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value, expected);
            }, failures);
        }
        foreach (var graph in new[] { GraphSearchLineageRf3Protocol.EarlyGraph, GraphSearchLineageRf3Protocol.LateGraph })
        {
            var request = new TraverseRequest(state.Partition, graph,
                GraphSearchLineageRf3Recipe.Vertex(state.Partition, GraphSearchLineageRf3Protocol.Alpha), 2,
                GraphSearchLineageRf3Protocol.Vertices, GraphSearchLineageRf3Protocol.Edges);
            var edges = state.Seed.Mutations.OfType<UpsertEdge>().Where(edge => edge.Graph == graph)
                .Select(edge => new EdgeRecord(edge.EdgeId, edge.From, edge.To, edge.Label, edge.AttributesJson, 1L)).ToArray();
            var vertices = graph == GraphSearchLineageRf3Protocol.EarlyGraph
                ? new[] { GraphSearchLineageRf3Protocol.Alpha, GraphSearchLineageRf3Protocol.Beta }
                : new[] { GraphSearchLineageRf3Protocol.Alpha, GraphSearchLineageRf3Protocol.Beta, GraphSearchLineageRf3Protocol.Context };
            var expected = new global::KeyLoad.GraphTraversal([.. vertices.Select(id => GraphSearchLineageRf3Recipe.Vertex(state.Partition, id))], [.. edges]);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlRf3Protocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.TraverseAsync(request, token)), expected);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlRf3Protocol.EqualAsync((await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await root.Mcp.CallAsync(
                    McpCallerTools.GraphTraverse, request, token))).Value, expected);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token)), state.Outbox);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(await root.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value, state.Outbox);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
