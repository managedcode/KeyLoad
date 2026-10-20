using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphTraversalForeignFrontierAssertions
{
    private const int TwoHops = 2;
    private const int MaximumVertices = 4;
    private const int MaximumEdges = 4;

    internal static async Task Healthy(DatabaseEngine owner, ZoneTreeStore store, GraphTraversalForeignFrontierState state,
        GraphTraversalIntegrityContinuation? continuation, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var actual = owner.Traverse(GraphCrossPartitionTestSupport.Root, state.Source.Partition,
            GraphCrossPartitionTestSupport.Graph, state.Source, TwoHops, MaximumVertices, MaximumEdges, cancellationToken: token);
        EdgeRecord[] edges = continuation is null ? [state.SourceEdge] : [state.SourceEdge, continuation.Edge];
        EntityRef[] vertices = continuation is null ? [state.Source, state.Foreign] : [state.Source, state.Foreign, continuation.Edge.To];
        await Assert.That(actual.Edges.SequenceEqual(edges.OrderBy(edge => edge.Id, StringComparer.Ordinal))).IsTrue();
        await Assert.That(actual.Vertices.SequenceEqual(vertices.OrderBy(vertex => vertex.Collection, StringComparer.Ordinal)
            .ThenBy(vertex => vertex.Id, StringComparer.Ordinal))).IsTrue();
        await GraphTraversalIntegrityAssertions.Image(store, image, position);
        foreach (var (command, receipt) in state.History)
        {
            await GraphTraversalIntegrityAssertions.Replay(owner, store, command, receipt);
        }
        if (continuation is not null)
        {
            await GraphTraversalIntegrityAssertions.Replay(owner, store, continuation.Command, continuation.Receipt);
        }
    }
}
