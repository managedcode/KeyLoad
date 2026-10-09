using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityFuture;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityFinalInstall
{
    internal static bool IsFinal(PartitionMoveParentState state, PartitionMovePhaseCommand phase)
    {
        if (phase.Stage != PartitionMovePeerStage.Install)
        { return false; }
        var descriptor = state.Selected?.OriginalDescriptor
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        return phase.PageOrdinal == descriptor.Families.Sum(static family => family.PageCount);
    }

    internal static long OutcomeCapacity(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        long successfulOutcome)
        => IsFinal(state, phase) ? Math.Max(successfulOutcome, checked(ReferenceFieldBytes
            * PartitionMovementFinalOutcomeCapacityProtocol.FailedResultReferenceFields + ScalarFieldBytes
            + Bytes(ZoneTreeStore.EncodedFrameLimitRejectionDetailBytes))) : successfulOutcome;

    internal static long StateCapacity(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        PhysicalShardRecord receiver, ImmutableArray<ResourceDefinition> resources, long grant, long journal)
    {
        var actual = PartitionMovementParentCapacityBounds.Bound(state);
        if (!IsFinal(state, phase))
        { return actual; }
        if (state.Pending is { Stage: not PartitionMovePeerStage.ControlAuthorize })
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var header = state.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var authorizeBody = Root<PartitionMoveAuthorizeBody>(checked(ReferenceFieldBytes
            + GuidFieldBytes * PartitionMovementFinalOutcomeCapacityProtocol.AuthorizationGuidFields
            + PartitionMovementParentCapacityBounds.Bound(header.OperatorPrincipalId)
            + PartitionMovementParentCapacityBounds.Bound(phase)
            + PartitionMovementParentCapacityBounds.Bound(receiver) + DateTimeFieldBytes));
        var futureAuthorization = checked(FuturePending(phase, header.ControlOwner, grant, journal)
            + PartitionMovementParentCapacityBounds.Bound(resources) + Bytes(authorizeBody)
            + FutureOutcome(FuturePhaseResult(state, journal, grant, ReferenceFieldBytes, ReferenceFieldBytes)));
        var previousPending = state.Pending is { } pending
            ? PartitionMovementParentCapacityBounds.Bound(pending) : ReferenceFieldBytes;
        var previousLast = state.LastIssued is { } last
            ? PartitionMovementParentCapacityBounds.Bound(last) : ReferenceFieldBytes;
        futureAuthorization = Math.Max(futureAuthorization, Math.Max(previousPending, previousLast));
        var previousPendingId = header.PendingOriginalPhaseCommandId is not null ? GuidFieldBytes : ReferenceFieldBytes;
        return checked(actual - previousPending - previousLast
            + futureAuthorization * PartitionMovementFinalOutcomeCapacityProtocol.AuthorizationPhaseSlots
            + GuidFieldBytes - previousPendingId);
    }
}
