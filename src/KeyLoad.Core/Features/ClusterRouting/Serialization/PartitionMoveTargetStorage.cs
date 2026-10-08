using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionMoveTargetStorage
{
    internal static byte[] Key(PartitionRef partition)
        => KeySpace.Partition(PartitionMoveProtocol.TargetStageSpace, partition);

    internal static byte[] PageKey(PartitionRef partition, Guid moveId, int ordinal)
        => KeyCodec.Encode(PartitionMoveProtocol.TargetPageSpace, partition.TenantId,
            partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey, moveId, ordinal);

    internal static T? Read<T>(IKeyValueView view, byte[] key, int maximumBytes) where T : class
    {
        T? result = null;
        view.ReadValue(key, bytes =>
        {
            if (bytes.Length > maximumBytes)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            result = NativeSerialization.Deserialize<T>(bytes);
        });
        return result;
    }

    internal static void Write<T>(IAtomicTransaction transaction, byte[] key, T record, int maximumBytes)
    {
        if (NativeSerialization.Measure(record) > maximumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        transaction.Put(key, NativeSerialization.Serialize(record));
    }
}
