using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.ResourceExecution;
using static KeyLoad.UnitTests.GraphTraversal.GraphReferenceLifetimeProtocol;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphReferenceLifetimeReads
{
    internal static async Task HealthyAsync(DropRuntimeProfile profile, string graph,
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
        var traversal = database.Traverse(PartitionStoreProtocol.AdministratorId, DropRuntimeProtocol.Partition,
            graph, from, Depth, Vertices, Edges, cancellationToken: token);
        await DropRuntimeAssertions.SameAsync(new global::KeyLoad.GraphTraversal([target, anchor], [edge]), traversal);
        var path = database.ShortestPath(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeProtocol.Version, DropRuntimeProtocol.Partition, graph, from, to, Depth, Vertices, Edges), cancellationToken: token);
        await DropRuntimeAssertions.SameAsync(new GraphShortestPathResult(GraphReferenceLifetimeProtocol.Version, true, Depth, [anchor, target], [edge], cut), path);
        var incoming = database.ReadIncomingGraphEdges(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeProtocol.Version, to, graph, Edges), cancellationToken: token);
        await DropRuntimeAssertions.SameAsync(new GraphIncomingEdgesPage(GraphReferenceLifetimeProtocol.Version, [new(edge, LocalDelivery)], cut, Projection), incoming);
        var result = await SearchAsync(profile, graph, from, token);
        await Assert.That(result.Hits.Length).IsEqualTo(Edges);
        await DropRuntimeAssertions.SameAsync(new DocumentResult(target, Revision,
            DropRuntimeProtocol.Json, false, []), result.Hits.Single().Document);
        await Assert.That(result.Hits.Single().Score).IsEqualTo(RetrieverWeight / (FusionConstant + FirstRank));
        await Assert.That(result.Expansion).IsNull();
    }

    internal static Task<GraphSearchResult> SearchAsync(DropRuntimeProfile profile, string graph,
        EntityRef anchor, CancellationToken token)
        => new SearchEngine(profile.Source.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
            PartitionStoreProtocol.AdministratorId, new(GraphReferenceLifetimeProtocol.Version,
                new(DropRuntimeProtocol.Partition, DropRuntimeProtocol.Table, FusionConstant: FusionConstant),
                Retriever: new(new(graph, [anchor], Depth, Vertices, Edges))), token);
}
