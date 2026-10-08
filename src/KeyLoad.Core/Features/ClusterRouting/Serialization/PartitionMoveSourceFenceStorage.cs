using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionMoveSourceFenceStorage
{
    internal static byte[] Key(PartitionRef partition)
        => KeySpace.Partition(PartitionMoveProtocol.SourceFenceSpace, partition);

    internal static PartitionMoveSourceFenceRecord? Read(IKeyValueView view, PartitionRef partition,
        int maximumEncodedBytes)
    {
        PartitionMoveSourceFenceRecord? record = null;
        view.ReadValue(Key(partition), bytes =>
        {
            if (bytes.Length > maximumEncodedBytes)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
            record = NativeSerialization.Deserialize<PartitionMoveSourceFenceRecord>(bytes);
            PartitionMoveSourceFenceValidation.Require(record, partition);
        });
        return record;
    }

    internal static void Write(IAtomicTransaction transaction, PartitionMoveSourceFenceRecord record,
        int maximumEncodedBytes)
    {
        PartitionMoveSourceFenceValidation.Require(record, record.Partition);
        if (NativeSerialization.Measure(record) > maximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        transaction.Put(Key(record.Partition), NativeSerialization.Serialize(record));
    }
}
