using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryMovementRows
{
    internal static byte[] ProofKey(PartitionRef partition)
        => KeySpace.Partition(PartitionRecordFamilies.ResourceOwnerInventoryProof, partition);

    internal static byte[] ImportKey(PartitionRef partition, Guid moveId)
        => KeyCodec.Encode(ResourceOwnerInventoryMovementProtocol.ImportSpace, partition.TenantId,
            partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey, moveId);

    internal static T? Read<T>(IKeyValueView view, byte[] key, DatabaseLimits limits) where T : class
    {
        var bytes = view.ReadOwnedValue(key);
        if (bytes is null)
        {
            return null;
        }
        if (bytes.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
        }
        return NativeSerialization.Deserialize<T>(bytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
    }

    internal static void Write<T>(IAtomicTransaction transaction, byte[] key, T value, DatabaseLimits limits)
    {
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryMovementProtocol.Capacity);
        }
        transaction.Put(key, bytes);
    }
}
