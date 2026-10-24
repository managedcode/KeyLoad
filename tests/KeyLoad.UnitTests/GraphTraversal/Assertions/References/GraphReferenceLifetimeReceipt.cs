using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.ResourceExecution;
using static KeyLoad.UnitTests.GraphTraversal.GraphReferenceLifetimeProtocol;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphReferenceLifetimeReceipt
{
    private const string UpsertMutation = "upsertEdge";

    internal static async Task AssertAsync(DropRuntimeProfile profile, CommandRequest original,
        CommitReceipt actual, string graph, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await profile.Source.Coordinator.ReadBarrierAsync(token);
        var key = OutcomeStoreOracle.PartitionKey(original.Partition, PartitionStoreProtocol.AdministratorId, original.CommandId);
        var outcome = profile.Source.Store.Read(view => view.GetRecord<StoredOutcome>(key))
            ?? throw new InvalidOperationException(DropRuntimeProtocol.MissingReply);
        await Assert.That(outcome.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(outcome.PreparedCommand.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(outcome.PreparedCommand.Value.Input.Span.SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new(profile.Source.Store.Identity.Incarnation, DropRuntimeProtocol.Partition.AtomicPartitionId,
                outcome.CanonicalSequence), [new(UpsertMutation, graph, EdgeId, Revision)], DurabilityProfile.ProcessDurable, DropRuntimeProtocol.PersistedCopies);
        await DropRuntimeAssertions.SameAsync(expected, actual);
    }
}
