namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryRf3MoveState(
    PartitionMoveRequest Request,
    PartitionMoveResult Terminal,
    ResourcePartitionInventoryRf3Seed Originals,
    ResourcePartitionInventoryRf3Baseline Before,
    ResourcePartitionInventoryRf3Baseline After);
