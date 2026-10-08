using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionMoveControlStorage
{
    private static byte[] ActiveKey(PartitionRef partition)
        => KeySpace.Partition(PartitionMoveProtocol.ActiveSpace, partition);

    private static byte[] RecordKey(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(PartitionMoveProtocol.RecordSpace, partition, moveId);

    internal static PartitionMoveControlRecord? Read(IKeyValueView view, PartitionRef partition,
        int maximumEncodedBytes)
    {
        Guid? moveId = null;
        view.ReadValue(ActiveKey(partition), bytes =>
        {
            RequireLength(bytes.Length, maximumEncodedBytes);
            moveId = NativeSerialization.Deserialize<Guid>(bytes);
        });
        return moveId is null ? null : ReadHistory(view, partition, moveId.Value, maximumEncodedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
    }

    internal static PartitionMoveControlRecord? ReadHistory(IKeyValueView view, PartitionRef partition,
        Guid moveId, int maximumEncodedBytes)
    {
        if (moveId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        PartitionMoveControlRecord? record = null;
        view.ReadValue(RecordKey(partition, moveId), bytes =>
        {
            RequireLength(bytes.Length, maximumEncodedBytes);
            record = NativeSerialization.Deserialize<PartitionMoveControlRecord>(bytes);
            PartitionMoveControlValidation.Require(record, partition);
            if (record.MoveId != moveId)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        });
        return record;
    }

    internal static void Write(IAtomicTransaction transaction, PartitionMoveControlRecord record,
        int maximumEncodedBytes)
    {
        PartitionMoveControlValidation.Require(record, record.Partition);
        if (NativeSerialization.Measure(record) > maximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        transaction.Put(RecordKey(record.Partition, record.MoveId), NativeSerialization.Serialize(record));
        transaction.Put(ActiveKey(record.Partition), NativeSerialization.Serialize(record.MoveId));
    }

    private static void RequireLength(int length, int maximumEncodedBytes)
    {
        if (length > maximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }
}
