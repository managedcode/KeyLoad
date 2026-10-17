namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ResourceInventoryImportResult(
    D2ResourceInventoryImportImage Image,
    ReplicatedOperation ConcurrentOperation,
    OperationResult ConcurrentResult,
    ReplicatedOperation InstalledOperation,
    OperationResult InstalledResult);
