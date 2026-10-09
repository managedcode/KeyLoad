using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

/// <summary>Strict native current-format parent reads and same-transaction acknowledged writes.</summary>
internal static class PartitionMoveParentStorage
{
    internal static PartitionMoveParentHeader? Header(IKeyValueView view, PartitionRef partition, Guid moveId,
        int maximumBytes)
    {
        var value = PartitionMoveTargetStorage.Read<PartitionMoveParentHeader>(view,
            PartitionMoveParentKeys.Header(partition, moveId), maximumBytes);
        if (value is not null)
        { PartitionMoveParentValidation.RequireHeader(value, partition, moveId); }
        return value;
    }

    internal static PartitionMoveParentPhase? Phase(IKeyValueView view, PartitionRef partition, Guid moveId,
        Guid originalPhaseId, int maximumBytes)
    {
        var value = PartitionMoveTargetStorage.Read<PartitionMoveParentPhase>(view,
            PartitionMoveParentKeys.Phase(partition, moveId, originalPhaseId), maximumBytes);
        if (value is not null)
        { PartitionMoveParentValidation.RequirePhase(value, partition, moveId, originalPhaseId); }
        return value;
    }

    internal static bool HasPhase(IKeyValueView view, PartitionRef partition, Guid moveId)
    {
        var found = false;
        view.VisitRange(PartitionMoveParentKeys.PhasePrefix(partition, moveId), PartitionMoveProtocol.SequenceStep,
            (_, _) => { found = true; return false; });
        return found;
    }

    internal static void WriteHeader(IAtomicTransaction transaction, PartitionMoveParentHeader value,
        int maximumBytes)
    {
        PartitionMoveParentValidation.RequireHeader(value, value.Partition, value.MoveId);
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveParentKeys.Header(value.Partition, value.MoveId),
            value, maximumBytes);
    }

    internal static void WritePhase(IAtomicTransaction transaction, PartitionMoveParentPhase value,
        int maximumBytes)
    {
        PartitionMoveParentValidation.RequirePhase(value, value.Partition, value.MoveId, value.OriginalPhaseCommandId);
        PartitionMoveTargetStorage.Write(transaction,
            PartitionMoveParentKeys.Phase(value.Partition, value.MoveId, value.OriginalPhaseCommandId), value, maximumBytes);
    }

    internal static long Counter(IKeyValueView view, byte[] key)
    {
        long value = PartitionMoveProtocol.EmptyCount;
        view.ReadValue(key, bytes => value = NativeSerialization.Deserialize<long>(bytes));
        if (value < PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        return value;
    }

    internal static void ChangeCounter(IAtomicTransaction transaction, byte[] key, long change, long maximum)
    {
        var changed = checked(Counter(transaction, key) + change);
        if (changed < PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        if (changed > maximum)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        transaction.Put(key, NativeSerialization.Serialize(changed));
    }
}
