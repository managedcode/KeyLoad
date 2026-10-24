using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphVertexAuthorityAssertions
{
    internal const string Detail = "A canonical graph vertex record is inconsistent.";
    private const string PutKind = "putDocument";
    private const string UpsertKind = "upsertEdge";

    internal static async Task<KeyLoadException> RefusedMutation(DatabaseEngine owner, ZoneTreeStore store,
        CommandRequest command)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => GraphStorageReferenceFixture.Submit(owner, command));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(Detail);
        await GraphTraversalIntegrityAssertions.Image(store, image, position);
        await ReplayFailure(owner, store, command, failure);
        return failure;
    }

    internal static async Task ReplayFailure(DatabaseEngine owner, ZoneTreeStore store,
        CommandRequest command, KeyLoadException original)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var replay = Assert.ThrowsExactly<KeyLoadException>(() => GraphStorageReferenceFixture.Submit(owner, command));
        await Assert.That(replay.Code).IsEqualTo(original.Code);
        await Assert.That(replay.Message).IsEqualTo(original.Message);
        await GraphTraversalIntegrityAssertions.Image(store, image, position);
    }

    internal static async Task RepairedReceipt(DatabaseEngine owner, GraphTraversalIntegrityState state,
        CommandRequest command, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(state.SeedReceipt.Durability);
        MutationReceipt[] expected = [new(PutKind, GraphTraversalIntegrityFixture.Nodes,
            GraphVertexAuthorityFlow.Witness, 1L), new(UpsertKind, GraphTraversalIntegrityFixture.Graph,
            state.DamagedEdge.Id, 2L)];
        await Assert.That(receipt.Mutations.SequenceEqual(expected)).IsTrue();
        var reference = state.HealthyEdge.From with { Id = GraphVertexAuthorityFlow.Witness };
        await Assert.That(JsonSerializer.Serialize(owner.GetDocument(GraphTraversalIntegrityFixture.Root, reference), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(new DocumentResult(reference, 1L, GraphVertexAuthorityFlow.Payload, false, []), JsonDefaults.Options));
    }

    internal static async Task ReadsRefuse(DatabaseEngine owner, ZoneTreeStore store,
        GraphTraversalIntegrityState state, CancellationToken token)
    {
        await GraphTraversalIntegrityAssertions.Refuse(owner, store, state, false, token);
        await GraphShortestPathIntegrityAssertions.Refuse(owner, store, state, false, token);
    }

    internal static async Task Healthy(DatabaseEngine owner, ZoneTreeStore store,
        GraphTraversalIntegrityState state, GraphTraversalIntegrityContinuation? continuation, CancellationToken token)
    {
        await GraphTraversalIntegrityAssertions.Healthy(owner, store, state, continuation, false, token);
        await GraphShortestPathIntegrityAssertions.Healthy(owner, store, state, continuation, false, token);
    }
}
