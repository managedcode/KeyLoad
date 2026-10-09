using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The real failed receiver result remains exact inside its acknowledged canonical observation.</summary>
internal static class PartitionMovementEmbeddedFailureRf3Assertions
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut control,
        PartitionMovementPublicParentRf3NativeCut[] allOwners, ReplicaEntry[] actualEntries)
    {
        var original = control.OriginalPhase!;
        var retained = original.OriginalResult!;
        await Assert.That(retained.SafeDetail).IsNotNull();
        await Assert.That(ZoneTreeStore.IsEncodedFrameLimitRejection(retained)).IsTrue();
        foreach (var receiver in allOwners.Where(cut => cut.Header is null))
        {
            await Assert.That(receiver.OriginalNativeResult).IsNotNull();
            await SqlRf3Protocol.EqualAsync(receiver.OriginalNativeResult, retained);
        }
        var receipt = original.ObservationCheckpointReceipt!;
        var entry = actualEntries.Single(entry => entry.Operation?.Id == receipt.CommandId);
        var operation = entry.Operation!;
        await Assert.That(operation.PrincipalId).IsEqualTo(control.Header!.OperatorPrincipalId);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.ControlCheckpoint);
        var body = NativeSerialization.Deserialize<PartitionMoveCheckpointBody>(phase.Body.Span);
        await Assert.That(body.Action).IsEqualTo(PartitionMoveCheckpointAction.Observe);
        await Assert.That(body.OriginalPhaseCommandId).IsEqualTo(precut.EffectId);
        await SqlRf3Protocol.EqualAsync(body.ObservedOriginalResult, retained);
        var outcome = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(control,
            KeySpace.PartitionOutcome(seed.Partition, operation.PrincipalId, operation.Id));
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await SqlRf3Protocol.EqualAsync(outcome.Partition, seed.Partition);
        await Assert.That(outcome.Result.Error).IsNull();
        var observed = outcome.Result.Get<PartitionMovePhaseResult>();
        await SqlRf3Protocol.EqualAsync(observed.Journal, receipt);
        await Assert.That(receipt.AppliedPosition).IsEqualTo(entry.Index);
        await Assert.That(receipt.AppliedPosition).IsLessThanOrEqualTo(PartitionMovementCapturePointerRf3Fault.Applied(control));
    }
}
