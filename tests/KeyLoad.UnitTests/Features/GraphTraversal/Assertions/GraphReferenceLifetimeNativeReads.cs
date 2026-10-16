using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.GraphTraversal.GraphReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphReferenceLifetimeNativeReads
{
    internal static async Task HealthyAsync(NativeDropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        var database = profile.Source.Database;
        var edge = new EdgeRecord(EdgeId, anchor, target, Label, Attributes, Revision);
        var from = anchor with
        {
            Lifetime = null
        };
        var to = target with
        {
            Lifetime = null
        };
        var cut = profile.Source.Store.Position;
        var traversal = database.Traverse(PartitionStoreProtocol.AdministratorId, NativeDropRuntimeProtocol.Partition,
            graph, from, Depth, Vertices, Edges, cancellationToken: token);
        await NativeDropRuntimeAssertions.SameAsync(new global::KeyLoad.GraphTraversal([anchor, target], [edge]), traversal);
        var path = database.ShortestPath(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeNativeProtocol.Version, NativeDropRuntimeProtocol.Partition, graph, from, to, Depth, Vertices, Edges), cancellationToken: token);
        await NativeDropRuntimeAssertions.SameAsync(new GraphShortestPathResult(GraphReferenceLifetimeNativeProtocol.Version, true, Depth, [anchor, target], [edge], cut), path);
        var incoming = database.ReadIncomingGraphEdges(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeNativeProtocol.Version, to, graph, Edges), cancellationToken: token);
        await NativeDropRuntimeAssertions.SameAsync(new GraphIncomingEdgesPageV1(GraphReferenceLifetimeNativeProtocol.Version, [new(edge, LocalDelivery)], cut, Projection), incoming);
        var result = await SearchAsync(profile, graph, from, token);
        await Assert.That(result.Hits.Length).IsEqualTo(Edges);
        await NativeDropRuntimeAssertions.SameAsync(new DocumentResult(target, Revision,
            NativeDropRuntimeProtocol.Json, false, []), result.Hits.Single().Document);
        await Assert.That(result.Hits.Single().Score).IsEqualTo(RetrieverWeight / (FusionConstant + FirstRank));
        await Assert.That(result.Expansion).IsNull();
    }

    internal static Task<GraphSearchResult> SearchAsync(NativeDropRuntimeProfile profile, string graph,
        EntityRef anchor, CancellationToken token)
        => new SearchEngine(profile.Source.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
            PartitionStoreProtocol.AdministratorId, new(GraphReferenceLifetimeNativeProtocol.Version,
                new(NativeDropRuntimeProtocol.Partition, NativeDropRuntimeProtocol.Table, FusionConstant: FusionConstant),
                Retriever: new(new(graph, [anchor], Depth, Vertices, Edges))), token);
}
