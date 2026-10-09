using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityTransportRecords
{
    internal static long Bound(PartitionMovePeerEnvelope value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.SourcePlacement) + PartitionMovementParentCapacityBounds.Bound(value.DestinationOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.ControlIntentDigest) + ScalarFieldBytes
            + ScalarFieldBytes + DateTimeFieldBytes
            + GuidFieldBytes + Bytes(value.Body.Length)
            + (value.Grant is { } presentvalueGrant ? PartitionMovementParentCapacityBounds.Bound(presentvalueGrant) : ReferenceFieldBytes) + (value.ReceiverIssuanceProof is { } presentvalueReceiverIssuanceProof ? PartitionMovementParentCapacityBounds.Bound(presentvalueReceiverIssuanceProof) : ReferenceFieldBytes)
            + (value.SourceDispatchWitness is { } presentvalueSourceDispatchWitness ? PartitionMovementParentCapacityBounds.Bound(presentvalueSourceDispatchWitness) : ReferenceFieldBytes));

    internal static long Bound(PartitionMovementTransportReply value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Receiver) + PartitionMovementParentCapacityBounds.Bound(value.Discovery)
            + PartitionMovementParentCapacityBounds.Bound(value.Reply) + PartitionMovementParentCapacityBounds.Bound(value.OriginalPhaseIdentityDigest));

    internal static long Bound(ReplicatedOperation value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.PrincipalId) + DateTimeFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.PayloadJson) + Bytes(value.NativePayload.Length));

    internal static long Bound(PartitionMoveAuthenticatedOutcomeWitness value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + GuidFieldBytes + GuidFieldBytes
            + Bytes(value.OriginalReplyBytes.Length) + PartitionMovementParentCapacityBounds.Bound(value.OriginalReplySignature));

    internal static long Bound(GrainOperationReply value)
        => checked(ReferenceFieldBytes
            + Bytes(value.Payload.Length) + (value.Error is not null ? ScalarFieldBytes : ReferenceFieldBytes)
            + PartitionMovementParentCapacityBounds.Bound(value.SafeDetail));

    internal static long Bound(PartitionMovementSourcePendingReply value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + DateTimeFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner) + PartitionMovementParentCapacityBounds.Bound(value.SourceOperatorPrincipalId)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Reply)
            + PartitionMovementParentCapacityBounds.Bound(value.Discovery));

    internal static long Bound(PartitionMoveCaptureWitness value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + Bytes(value.OriginalReplyBytes.Length)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalReplySignature));
}
