using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphTraversalIntegrityAssertions
{
    internal static async Task Refuse(DatabaseEngine owner, ZoneTreeStore store, GraphTraversalIntegrityState state,
        bool filtered, CancellationToken cancellationToken)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Read(owner, state, filtered, cancellationToken));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Image(store, image, position);
    }

    internal static async Task Healthy(DatabaseEngine owner, ZoneTreeStore store, GraphTraversalIntegrityState state,
        GraphTraversalIntegrityContinuation? continuation, bool filtered, CancellationToken cancellationToken)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var actual = Read(owner, state, filtered, cancellationToken);
        EdgeRecord[] allEdges = continuation is null ? [state.HealthyEdge, state.DamagedEdge]
            : [state.HealthyEdge, state.DamagedEdge, continuation.Edge];
        var expectedEdges = filtered ? [] : allEdges.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
        var expectedVertices = filtered ? [state.HealthyEdge.From]
            : allEdges.Select(edge => edge.To).Append(state.HealthyEdge.From)
                .OrderBy(vertex => vertex.Collection, StringComparer.Ordinal).ThenBy(vertex => vertex.Id, StringComparer.Ordinal).ToArray();
        await Assert.That(actual.Edges.SequenceEqual(expectedEdges)).IsTrue();
        await Assert.That(actual.Vertices.SequenceEqual(expectedVertices)).IsTrue();
        await Image(store, image, position);
    }

    internal static async Task Replay(DatabaseEngine owner, ZoneTreeStore store, CommandRequest command, CommitReceipt receipt)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var replay = GraphStorageReferenceFixture.Submit(owner, command).Get<CommitReceipt>();
        await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(receipt, JsonDefaults.Options));
        await Image(store, image, position);
    }

    internal static async Task Image(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image)).IsTrue();
    }

    private static global::KeyLoad.GraphTraversal Read(DatabaseEngine owner, GraphTraversalIntegrityState state,
        bool filtered, CancellationToken cancellationToken)
        => owner.Traverse(GraphTraversalIntegrityFixture.Root, state.HealthyEdge.From.Partition,
            GraphTraversalIntegrityFixture.Graph, state.HealthyEdge.From, GraphTraversalIntegrityFixture.Depth,
            GraphTraversalIntegrityFixture.MaximumVertices, GraphTraversalIntegrityFixture.MaximumEdges,
            filtered ? [GraphTraversalIntegrityFixture.OtherLabel] : null, cancellationToken);
}
