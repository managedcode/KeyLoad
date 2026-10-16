using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceReservationAssertions
{
    internal static async Task RecordAsync(ResourceRetirementQuiescenceState state, ControlledPartitionMovementNode owner,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, OperationResult result, CancellationToken token)
    {
        var reservation = result.Get<ResourceRetirementPhaseResultV1>().OwnerReservation ?? throw new InvalidOperationException(Invalid);
        var original = state.Originals.Last().Original;
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementQuiescenceData.Version, state.Request.RetirementId,
            ResourceRetirementPhase.ReserveOwner, original.Id, state.Control.Lifetime, owner.PhysicalOwner,
            owner.Journal.Log.State.LastIndex, state.Control.IntentDigest);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(receipt, reservation.Receipt);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(state.Control, reservation.Control);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(context.Receiver!, reservation.Receiver);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(attempt, reservation.Attempt!);
        await Assert.That(reservation.OriginalFingerprint).IsEqualTo(NativeOperationFingerprint.Compute(original));
        var local = ReferenceEquals(owner, state.Source)
            ? new[] { new ResourceRetirementAcceptedMoveV1(Partition, ControlledPartitionMovementPrepareRequest.MoveId) }
            : Array.Empty<ResourceRetirementAcceptedMoveV1>();
        await Assert.That(reservation.LocalAcceptedMoves.SequenceEqual(local)).IsTrue();
        var id = ResourceRetirementIdentity.AttemptCommandId(state.Request, ResourceRetirementPhase.RecordReservation,
            attempt.AttemptId, owner.PhysicalOwner.PhysicalShardId);
        await ResourceRetirementQuiescencePhases.SubmitAsync(state, state.Source, ResourceRetirementPhase.RecordReservation,
            id, new ResourceRetirementRecordReservationBodyV1(state.Control, reservation), token);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(attempt with
        {
            Stage = ResourceRetirementAttemptStage.Completed,
            OriginalFingerprint = reservation.OriginalFingerprint,
            TerminalReceipt = receipt
        }, state.Source.Database.ReadResourceRetirementAttempt(Root, context, attempt.AttemptId,
            ResourceRetirementQuiescencePhases.Work(state.Source, token)));
    }
}
