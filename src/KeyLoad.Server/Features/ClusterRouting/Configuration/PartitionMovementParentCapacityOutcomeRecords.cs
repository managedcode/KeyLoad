using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityOutcomeRecords
{
    internal static long Bound(PartitionMovePhaseResult value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Journal) + (value.Control is { } presentvalueControl ? PartitionMovementParentCapacityBounds.Bound(presentvalueControl) : ReferenceFieldBytes)
            + (value.Fence is { } presentvalueFence ? PartitionMovementParentCapacityBounds.Bound(presentvalueFence) : ReferenceFieldBytes) + (value.InstalledReceipt is { } presentvalueInstalledReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueInstalledReceipt) : ReferenceFieldBytes)
            + (value.PublishedPlacement is { } presentvaluePublishedPlacement ? PartitionMovementParentCapacityBounds.Bound(presentvaluePublishedPlacement) : ReferenceFieldBytes) + (value.Grant is { } presentvalueGrant ? PartitionMovementParentCapacityBounds.Bound(presentvalueGrant) : ReferenceFieldBytes)
            + (value.Cleanup is { } presentvalueCleanup ? PartitionMovementParentCapacityBounds.Bound(presentvalueCleanup) : ReferenceFieldBytes) + (value.ControlledCommand is { } presentvalueControlledCommand ? PartitionMovementParentCapacityBounds.Bound(presentvalueControlledCommand) : ReferenceFieldBytes)
            + (value.ControlledEffect is { } presentvalueControlledEffect ? PartitionMovementParentCapacityBounds.Bound(presentvalueControlledEffect) : ReferenceFieldBytes) + (value.ReceiverIssuance is { } presentvalueReceiverIssuance ? PartitionMovementParentCapacityBounds.Bound(presentvalueReceiverIssuance) : ReferenceFieldBytes)
            + (value.RetireCancellation is { } presentvalueRetireCancellation ? PartitionMovementParentCapacityBounds.Bound(presentvalueRetireCancellation) : ReferenceFieldBytes));

    internal static long Bound(PartitionMoveResult value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Partition)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.SourceOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.DestinationOwner) + ScalarFieldBytes
            + (value.InstalledReceipt is { } presentvalueInstalledReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueInstalledReceipt) : ReferenceFieldBytes) + (value.PublishedPlacement is { } presentvaluePublishedPlacement ? PartitionMovementParentCapacityBounds.Bound(presentvaluePublishedPlacement) : ReferenceFieldBytes));

    internal static long Bound(OperationResult value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Json) + (value.Error is not null ? ScalarFieldBytes : ReferenceFieldBytes)
            + PartitionMovementParentCapacityBounds.Bound(value.SafeDetail) + (value.NativeValue is { } presentvalueNativeValue ? BoundNativeValue(presentvalueNativeValue) : ReferenceFieldBytes));

    internal static long Bound(MutationReceipt value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Kind) + PartitionMovementParentCapacityBounds.Bound(value.Resource)
            + PartitionMovementParentCapacityBounds.Bound(value.Id) + ScalarFieldBytes);

    internal static long Bound(CommitToken value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.AtomicPartitionId)
            + ScalarFieldBytes + ScalarFieldBytes);

    internal static long Bound(PartitionMoveJournalReceipt value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.PhysicalOwner)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.ControlIntentDigest)
            + PartitionMovementParentCapacityBounds.Bound(value.EffectDigest));

    internal static long Bound(CommitReceipt value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Token)
            + PartitionMovementParentCapacityBounds.Bound(value.Mutations) + ScalarFieldBytes);
}
