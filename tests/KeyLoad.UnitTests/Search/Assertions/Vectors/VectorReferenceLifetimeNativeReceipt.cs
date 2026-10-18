using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.VectorReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class VectorReferenceLifetimeNativeReceipt
{
    private const string ProjectionMutation = "applyVectorProjection";

    internal static async Task AssertAsync(NativeDropRuntimeProfile profile, CommandRequest original,
        CommitReceipt actual, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await profile.Source.Coordinator.ReadBarrierAsync(token);
        var key = OutcomeStoreOracle.PartitionKey(original.Partition, PartitionStoreProtocol.AdministratorId, original.CommandId);
        var outcome = profile.Source.Store.Read(view => view.GetRecord<StoredOutcome>(key))
            ?? throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingReply);
        await Assert.That(outcome.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(outcome.PreparedCommand.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(outcome.PreparedCommand.Value.Input.Span.SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new(profile.Source.Store.Identity.Incarnation, NativeDropRuntimeProtocol.Partition.AtomicPartitionId,
                outcome.CanonicalSequence), [new(ProjectionMutation, Target, NativeDropRuntimeProtocol.Row, FirstRevision)], DurabilityProfile.ProcessDurable, NativeDropRuntimeProtocol.PersistedCopies);
        await NativeDropRuntimeAssertions.SameAsync(expected, actual);
    }
}
