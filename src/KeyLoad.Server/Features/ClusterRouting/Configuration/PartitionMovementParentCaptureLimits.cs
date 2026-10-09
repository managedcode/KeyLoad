using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCaptureLimits
{
    internal static PartitionMoveCaptureRequest Create(string principalId, PartitionMoveSourceFenceRecord fence,
        DatabaseLimits limits, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(work);
        limits.Validate();
        work.Check();
        var bytes = Math.Min(limits.MaxQueryReadBytes, work.RemainingReadGrantBytes);
        var records = Math.Min(limits.MaxScanRecords, work.RemainingReadGrantRecords);
        var pageBytes = Math.Min(limits.MaxBatchBytes, work.MaximumResultBytes);
        if (bytes <= PartitionMoveProtocol.EmptyCount || records <= PartitionMoveProtocol.EmptyCount
            || pageBytes <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return new(principalId, fence, pageBytes, bytes, records);
    }

    internal static void RequireReceiverIssueCapacity(PartitionMoveReceiverIssueCapacityContext context,
        string actualClusterId)
        => PartitionMovementParentCapacityAdmission.RequireReceiverIssueCapacity(context, actualClusterId);

    internal static void RequirePlannedStageCapacity(PartitionMoveParentState state,
        PartitionMovePhaseCommand phase, PhysicalShardRecord receiver, ImmutableArray<ResourceDefinition> resources,
        string actualClusterId, long maximumBytes)
        => PartitionMovementParentCapacityAdmission.RequirePlannedStageCapacity(state, phase, receiver, resources,
            actualClusterId, maximumBytes);
}
