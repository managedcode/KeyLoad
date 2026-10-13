namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record TenantDedicatedOwnerRf3State(
    RequestCqrsPhaseFaultIdentity Sibling,
    CommandRequest SiblingSeed,
    CommitReceipt SiblingReceipt,
    AtomicPartitionPlacementResolution Original,
    AtomicPartitionPlacementResolution SiblingPlacement,
    PartitionMoveRequest Move,
    PartitionMoveRequest DeniedMove,
    NodeStatus[] Physical);
