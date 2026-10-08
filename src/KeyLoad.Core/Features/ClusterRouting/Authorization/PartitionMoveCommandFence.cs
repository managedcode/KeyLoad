using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireNoPartitionMovementFence(IKeyValueView view, ReplicatedOperation operation,
        CommandOutcomePartitionScope scope)
    {
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        { return; }
        if (configuredPhysicalOwner is null)
        { RequireUnconfiguredMovementAbsent(view); return; }
        if (scope.Partition is not { } partition)
        { return; }
        RequireNoUnpublishedPartitionMoveTarget(view, partition);
        var fence = PartitionMoveSourceFenceStorage.Read(view, partition, Limits.MaxBatchBytes);
        if (fence is not null)
        {
            PartitionMoveSourceFenceValidation.Require(fence, partition);
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced);
        }
    }
}
