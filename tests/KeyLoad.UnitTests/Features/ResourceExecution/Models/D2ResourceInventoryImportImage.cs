using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ResourceInventoryImportImage(
    PartitionMovementCaptureHandle Handle,
    PartitionMovementPageResult[] Pages,
    PartitionMovePhaseResult Captured,
    PartitionMoveSourceFenceRecord Fence);

internal sealed record D2ResourceInventoryImportResult(
    D2ResourceInventoryImportImage Image,
    ReplicatedOperation ConcurrentOperation,
    OperationResult ConcurrentResult,
    ReplicatedOperation InstalledOperation,
    OperationResult InstalledResult);
