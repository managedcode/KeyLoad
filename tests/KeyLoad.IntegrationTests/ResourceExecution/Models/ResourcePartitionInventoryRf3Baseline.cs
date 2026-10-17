namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryRf3Baseline(
    ResourceOwnerInventoryResultV1 First, ResourceOwnerInventoryResultV1 Second,
    ResourceOwnerInventoryResultV1 Unused);
