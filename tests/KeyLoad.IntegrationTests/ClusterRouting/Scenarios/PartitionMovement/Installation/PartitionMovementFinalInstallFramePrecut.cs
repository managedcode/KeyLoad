using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PartitionMovementFinalInstallFramePrecut(PartitionMovementPublicParentRf3NativeCut[] Cuts,
    Dictionary<string, NodeStatus> Owners, PartitionMoveParentState State, int FinalOrdinal, Guid GrantId, Guid EffectId,
    PartitionMovementStoppedNativeSnapshot Calibrated, PartitionMovementStoppedNativeSnapshot Refused);
