using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingVertexAuthorityAssertions
{
    internal static async Task Refuse(DatabaseEngine owner, ZoneTreeStore store,
        GraphTraversalIntegrityState state, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Read(owner, state.DamagedEdge.To, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(GraphVertexAuthorityAssertions.Detail);
        await GraphTraversalIntegrityAssertions.Image(store, image, cut);
    }

    internal static async Task Healthy(DatabaseEngine owner, ZoneTreeStore store,
        GraphTraversalIntegrityState state, EdgeRecord? continuation, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var actual = Read(owner, state.DamagedEdge.To, token);
        GraphIncomingEdgeRow[] rows = continuation is null
            ? [new(state.HealthyEdge, 0L), new(state.DamagedEdge, 0L)]
            : [new(state.HealthyEdge, 0L), new(state.DamagedEdge, 0L), new(continuation, 0L)];
        var expected = new GraphIncomingEdgesPage(1, [.. rows], cut, "eventual-reverse");
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await GraphTraversalIntegrityAssertions.Image(store, image, cut);
    }

    internal static async Task Continuation(DatabaseEngine owner, CommandRequest command,
        CommitReceipt receipt, EdgeRecord edge)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(owner.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        MutationReceipt[] expected = [new("putDocument", GraphTraversalIntegrityFixture.Nodes, "incoming-witness", 1L),
            new("upsertEdge", GraphTraversalIntegrityFixture.Graph, edge.Id, 1L)];
        await Assert.That(receipt.Mutations.SequenceEqual(expected)).IsTrue();
        var reference = new EntityRef(command.Partition, GraphTraversalIntegrityFixture.Nodes, "incoming-witness");
        await Assert.That(JsonSerializer.Serialize(owner.GetDocument(GraphTraversalIntegrityFixture.Root, reference), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(new DocumentResult(reference, 1L, "{}", false, []), JsonDefaults.Options));
    }

    private static GraphIncomingEdgesPage Read(DatabaseEngine owner, EntityRef target, CancellationToken token)
        => owner.ReadIncomingGraphEdges(GraphTraversalIntegrityFixture.Root,
            new(1, target, GraphTraversalIntegrityFixture.Graph, 4), cancellationToken: token);
}
