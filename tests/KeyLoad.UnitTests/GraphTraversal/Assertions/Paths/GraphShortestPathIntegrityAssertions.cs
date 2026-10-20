using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphShortestPathIntegrityAssertions
{
    private const int Version = 1;
    private const int OneHop = 1;

    internal static async Task Refuse(DatabaseEngine owner, ZoneTreeStore store, GraphTraversalIntegrityState state,
        bool filtered, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => owner.ShortestPath(GraphTraversalIntegrityFixture.Root,
            Request(state.DamagedEdge, filtered), cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await GraphTraversalIntegrityAssertions.Image(store, image, position);
    }

    internal static async Task Healthy(DatabaseEngine owner, ZoneTreeStore store, GraphTraversalIntegrityState state,
        GraphTraversalIntegrityContinuation? continuation, bool filtered, CancellationToken token)
    {
        await Path(owner, store, state.DamagedEdge, filtered, token);
        if (continuation is not null)
        {
            await Path(owner, store, continuation.Edge, filtered, token);
        }
    }

    private static async Task Path(DatabaseEngine owner, ZoneTreeStore store, EdgeRecord edge,
        bool filtered, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var actual = owner.ShortestPath(GraphTraversalIntegrityFixture.Root, Request(edge, filtered), cancellationToken: token);
        await Assert.That(actual.Version).IsEqualTo(Version);
        await Assert.That(actual.Found).IsEqualTo(!filtered);
        await Assert.That(actual.Hops).IsEqualTo(filtered ? (int?)null : OneHop);
        EntityRef[] vertices = filtered ? [] : [edge.From, edge.To];
        EdgeRecord[] edges = filtered ? [] : [edge];
        await Assert.That(actual.Vertices.SequenceEqual(vertices)).IsTrue();
        await Assert.That(actual.Edges.SequenceEqual(edges)).IsTrue();
        await Assert.That(actual.CutPosition).IsEqualTo(position);
        await GraphTraversalIntegrityAssertions.Image(store, image, position);
    }

    private static GraphShortestPathRequest Request(EdgeRecord edge, bool filtered)
        => new(Version, edge.From.Partition, GraphTraversalIntegrityFixture.Graph, edge.From, edge.To,
            GraphTraversalIntegrityFixture.Depth, GraphTraversalIntegrityFixture.MaximumVertices,
            GraphTraversalIntegrityFixture.MaximumEdges)
        {
            Labels = filtered ? [GraphTraversalIntegrityFixture.OtherLabel] : []
        };
}
