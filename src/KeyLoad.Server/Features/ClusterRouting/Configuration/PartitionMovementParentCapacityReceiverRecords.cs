using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityReceiverRecords
{
    internal static long Bound(PartitionMoveReceiverSourceWitness value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + Bytes(value.OriginalReplyBytes.Length)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalReplySignature));

    internal static long Bound(PartitionMovementReceiverIssuanceResult value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Snapshot) + ScalarFieldBytes
            + ScalarFieldBytes);

    internal static long Bound(PartitionMovementReceiverIssueRequest value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.OriginalEnvelope)
            + PartitionMovementParentCapacityBounds.Bound(value.SourceWitness) + PartitionMovementParentCapacityBounds.Bound(value.OriginalAuthorization)
            + PartitionMovementParentCapacityBounds.Bound(value.CallerVoter) + PartitionMovementParentCapacityBounds.Bound(value.CallerSiloAddress)
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.SourceProofCheckpointReceipt));

    internal static long Bound(PartitionMoveReceiverIssuance value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalPhaseIdentityDigest) + GuidFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.SourceOperatorPrincipalId)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.ReceiverPrincipalId)
            + ScalarFieldBytes + DateTimeFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.ReceiverOwner) + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalAuthorization) + PartitionMovementParentCapacityBounds.Bound(value.OriginalBodyDigest)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalControlIntentDigest) + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.IssuanceFingerprint));

    internal static long Bound(PartitionMoveReceiverIssuanceWitness value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + Bytes(value.OriginalReplyBytes.Length)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalReplySignature));

    internal static long Bound(PartitionMoveReceiverIssuePacket value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + GuidFieldBytes + Bytes(value.OriginalRequestBytes.Length)
            + PartitionMovementParentCapacityBounds.Bound(value.OriginalRequestSignature));

    internal static long Bound(PartitionMoveReceiverEffectAdmission value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + DateTimeFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.OriginalGrant)
            + PartitionMovementParentCapacityBounds.Bound(value.ReceiverWitness) + PartitionMovementParentCapacityBounds.Bound(value.SourceDispatchWitness));

    internal static long Bound(PartitionMoveReceiverIssuanceSnapshot value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Issuance) + PartitionMovementParentCapacityBounds.Bound(value.OriginalResult)
            + ScalarFieldBytes);
}
