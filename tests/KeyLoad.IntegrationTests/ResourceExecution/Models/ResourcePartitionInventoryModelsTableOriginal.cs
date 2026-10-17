namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryModelsTableOriginal(SqlOperationRequest Create,
    ResourceDefinition Definition, SqlOperationRequest Insert, CommitReceipt Receipt);
