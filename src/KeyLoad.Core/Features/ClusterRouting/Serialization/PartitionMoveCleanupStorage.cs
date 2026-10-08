using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionMoveCleanupStorage
{
    internal static byte[] Key(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(PartitionMoveProtocol.CleanupSpace, partition, moveId);

    internal static PartitionMoveCleanupState? Read(IKeyValueView view, PartitionRef partition,
        Guid moveId, int maximumBytes)
    {
        var state = PartitionMoveTargetStorage.Read<PartitionMoveCleanupState>(view,
            Key(partition, moveId), maximumBytes);
        if (state is not null && (state.Version != PartitionMoveProtocol.Version
            || state.MoveId != moveId || state.Partition != partition
            || !Enum.IsDefined(state.Role) || state.NextFamily < PartitionMoveProtocol.EmptyCount
            || state.NextFamily > PartitionMoveCleanupFamilies.All.Length))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        return state;
    }

    internal static void RequireOpen(IKeyValueView view, PartitionRef partition, Guid moveId, int maximumBytes)
    {
        if (Read(view, partition, moveId, maximumBytes) is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced); }
    }
}
