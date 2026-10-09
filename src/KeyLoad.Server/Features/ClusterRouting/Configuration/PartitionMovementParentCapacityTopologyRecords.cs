using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityTopologyRecords
{
    internal static long Bound(PartitionMoveCleanupState value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.ControlIntentDigest)
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + (value.Completion is { } presentvalueCompletion ? PartitionMovementParentCapacityBounds.Bound(presentvalueCompletion) : ReferenceFieldBytes)
            + ScalarFieldBytes);

    internal static long Bound(PartitionMoveImageFamily value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Family) + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Digest));

    internal static long Bound(PartitionMoveSourceFenceRecord value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner)
            + PartitionMovementParentCapacityBounds.Bound(value.SourcePlacement) + PartitionMovementParentCapacityBounds.Bound(value.DestinationOwner)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.ControlIntentDigest));

    internal static long Bound(PhysicalOwnerDirectoryV1 value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.ControlOwner) + PartitionMovementParentCapacityBounds.Bound(value.Owners));

    internal static long Bound(AtomicPartitionPlacementResolution value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Partition)
            + GuidFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.VoterIds) + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes);

    internal static long Bound(ReplicaSiloDiscovery value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.VoterId) + PartitionMovementParentCapacityBounds.Bound(value.ClusterId)
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.SiloAddress)
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes);

    internal static long Bound(AtomicPartitionPlacementV1 value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Partition)
            + GuidFieldBytes + ScalarFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.VoterIds)
            + ScalarFieldBytes);

    internal static long Bound(PartitionMoveRequest value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Partition)
            + GuidFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes);

    internal static long Bound(PartitionRef value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.TenantId) + PartitionMovementParentCapacityBounds.Bound(value.DatabaseId)
            + PartitionMovementParentCapacityBounds.Bound(value.TransactionDomainId) + PartitionMovementParentCapacityBounds.Bound(value.PartitionKey));

    internal static long Bound(PartitionMoveControlRecord value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.PrincipalId)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.SourcePlacement)
            + PartitionMovementParentCapacityBounds.Bound(value.DestinationOwner) + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.ImageDigest) + (value.InstalledReceipt is { } presentvalueInstalledReceipt ? PartitionMovementParentCapacityBounds.Bound(presentvalueInstalledReceipt) : ReferenceFieldBytes)
            + (value.PublishedPlacement is { } presentvaluePublishedPlacement ? PartitionMovementParentCapacityBounds.Bound(presentvaluePublishedPlacement) : ReferenceFieldBytes) + ScalarFieldBytes);

    internal static long Bound(PartitionMoveImageDescriptor value)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Partition) + PartitionMovementParentCapacityBounds.Bound(value.SourcePlacement)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Families)
            + PartitionMovementParentCapacityBounds.Bound(value.Digest) + PartitionMovementParentCapacityBounds.Bound(value.Resources));

    internal static long Bound(PhysicalShardRecord value)
        => checked(ReferenceFieldBytes
            + GuidFieldBytes + GuidFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.VoterIds) + ScalarFieldBytes);

    internal static long Bound(RegisteredPhysicalOwnerV1 value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Owner) + PartitionMovementParentCapacityBounds.Bound(value.Endpoints));
}
