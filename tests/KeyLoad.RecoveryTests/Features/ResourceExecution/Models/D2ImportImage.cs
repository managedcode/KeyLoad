using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal sealed class D2ImportImage(PartitionMovePhaseResult captured, PartitionMoveSourceFenceRecord fence,
    PartitionMovementCaptureHandle handle, PartitionMovementPageResult[] pages)
{
    internal PartitionMovePhaseResult Captured { get; } = captured;
    internal PartitionMoveSourceFenceRecord Fence { get; } = fence;
    internal PartitionMovementCaptureHandle Handle { get; } = handle;
    internal PartitionMovementPageResult[] Pages { get; } = pages;
    internal int NextOrdinal
    {
        get; set;
    }
    internal List<(ReplicatedOperation Original, OperationResult Result)> Installs { get; } = [];
}
