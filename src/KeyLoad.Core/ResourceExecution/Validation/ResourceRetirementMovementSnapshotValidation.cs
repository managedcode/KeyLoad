using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementMovementSnapshotValidation
{
    internal static PartitionMoveCleanupState Cleanup(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var state = NativeSerialization.Deserialize<PartitionMoveCleanupState>(value);
        if (state is null || state.Version != PartitionMoveProtocol.Version || state.Partition is null
            || state.MoveId == Guid.Empty || !Enum.IsDefined(state.Role)
            || state.Stage is not (PartitionMovePeerStage.Retire or PartitionMovePeerStage.Abort)
            || state.NextFamily < PartitionMoveProtocol.EmptyCount || state.NextFamily > PartitionMoveCleanupFamilies.All.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        DatabaseEngine.ValidatePartition(state.Partition);
        ResourceRetirementMetadataScan.Exact(key, PartitionMoveCleanupStorage.Key(state.Partition, state.MoveId));
        return state;
    }
}
