using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PartitionMovementParentOperationalCapacityPrecut(PartitionMovementPublicParentRf3NativeCut[] Cuts,
    PartitionMoveParentState State, PartitionMovementStoppedNativeSnapshot Inspected,
    PartitionMovementStoppedNativeSnapshot Refused);
