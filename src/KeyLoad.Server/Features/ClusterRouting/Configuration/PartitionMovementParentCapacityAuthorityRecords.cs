using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityAuthorityRecords
{
    internal static long Bound(PartitionControlDelegation value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Identity)
            + PartitionMovementParentCapacityBounds.Bound(value.Fingerprint) + GuidFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.TargetPlacement)
            + PartitionMovementParentCapacityBounds.Bound(value.Principal) + DateTimeFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Resources)
            + (value.ControlOwner is { } presentvalueControlOwner ? PartitionMovementParentCapacityBounds.Bound(presentvalueControlOwner) : ReferenceFieldBytes) + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalPayloadJson) + (value.OriginalOperation is { } presentvalueOriginalOperation ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalOperation) : ReferenceFieldBytes));

    internal static long Bound(PartitionMoveRetireCancellationAttempt value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + DateTimeFieldBytes
            + Bytes(value.OriginalRequestBytes.Length) + PartitionMovementParentCapacityBounds.Bound(value.OriginalRequestSignature));

    internal static long Bound(PartitionMoveRetireCancellationWitness value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Cancellation) + Bytes(value.OriginalReplyBytes.Length)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalReplySignature));

    internal static long Bound(PartitionMoveRetireCancellationDisposition value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalPhaseIdentityDigest) + GuidFieldBytes
            + DateTimeFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.CancellationReceipt) + PartitionMovementParentCapacityBounds.Bound(value.ReceiverPrincipalId)
            + ScalarFieldBytes + ScalarFieldBytes);

    internal static long Bound(PartitionControlEffectPayload value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Receipt) + PartitionMovementParentCapacityBounds.Bound(value.OriginalResult));

    internal static long Bound(PartitionControlCommandIdentity value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + (value.Partition is { } presentvaluePartition ? PartitionMovementParentCapacityBounds.Bound(presentvaluePartition) : ReferenceFieldBytes)
            + PartitionMovementParentCapacityBounds.Bound(value.PrincipalId) + GuidFieldBytes);

    internal static long Bound(PartitionMovePhaseGrant value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.ReceiverOwner) + PartitionMovementParentCapacityBounds.Bound(value.OperatorPrincipalId)
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.ControlIntentDigest) + PartitionMovementParentCapacityBounds.Bound(value.BodyDigest)
            + DateTimeFieldBytes + ScalarFieldBytes
            + (value.Settlement is { } presentvalueSettlement ? PartitionMovementParentCapacityBounds.Bound(presentvalueSettlement) : ReferenceFieldBytes) + PartitionMovementParentCapacityBounds.Bound(value.Resources)
            + (value.AbortDisposition is { } presentvalueAbortDisposition ? PartitionMovementParentCapacityBounds.Bound(presentvalueAbortDisposition) : ReferenceFieldBytes) + (value.CleanupRole is not null ? ScalarFieldBytes : ReferenceFieldBytes)
            + (value.CleanupFamily is not null ? ScalarFieldBytes : ReferenceFieldBytes) + (value.PrecedingGrantId is not null ? GuidFieldBytes : ReferenceFieldBytes)
            + ScalarFieldBytes + ScalarFieldBytes
            + (value.RetireCancellationDisposition is { } presentvalueRetireCancellationDisposition ? PartitionMovementParentCapacityBounds.Bound(presentvalueRetireCancellationDisposition) : ReferenceFieldBytes));

    internal static long Bound(PartitionControlCommandRecord value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Identity)
            + PartitionMovementParentCapacityBounds.Bound(value.Fingerprint) + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.Destination) + GuidFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + (value.TargetEffect is { } presentvalueTargetEffect ? PartitionMovementParentCapacityBounds.Bound(presentvalueTargetEffect) : ReferenceFieldBytes) + PartitionMovementParentCapacityBounds.Bound(value.TargetEffectDigest)
            + (value.OriginalOutcome is { } presentvalueOriginalOutcome ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalOutcome) : ReferenceFieldBytes) + (value.OriginalResult is { } presentvalueOriginalResult ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalResult) : ReferenceFieldBytes)
            + (value.Delegation is { } presentvalueDelegation ? PartitionMovementParentCapacityBounds.Bound(presentvalueDelegation) : ReferenceFieldBytes) + (value.OriginalOperation is { } presentvalueOriginalOperation ? PartitionMovementParentCapacityBounds.Bound(presentvalueOriginalOperation) : ReferenceFieldBytes)
            + Bytes(value.TargetBody.Length));

    internal static long Bound(PartitionMoveExpiredRetireCancellation value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalPhaseIdentityDigest) + GuidFieldBytes
            + DateTimeFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.CancellationReceipt) + PartitionMovementParentCapacityBounds.Bound(value.CancellationPrincipalId)
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.CancellationPhase));

    internal static long Bound(PartitionControlOutcomeReference value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Identity)
            + PartitionMovementParentCapacityBounds.Bound(value.Fingerprint) + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner)
            + Bytes(value.OutcomeKey.Length) + PartitionMovementParentCapacityBounds.Bound(value.OutcomeDigest));
}
