using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ResourceInventoryImportImage(
    PartitionMovementCaptureHandle Handle,
    PartitionMovementPageResult[] Pages,
    PartitionMovePhaseResult Captured,
    PartitionMoveSourceFenceRecord Fence);
