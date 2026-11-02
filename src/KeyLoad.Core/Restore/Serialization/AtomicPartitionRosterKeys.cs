using KeyLoad.Storage;

namespace KeyLoad.Core.BackupRestore.Serialization;

internal static class AtomicPartitionRosterKeys
{
    internal static byte[] Prefix() => KeyCodec.Encode(AtomicPartitionRosterProtocol.KeySpace,
        AtomicPartitionRosterProtocol.PartitionKeyKind);

    internal static byte[] Partition(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return KeyCodec.Encode(AtomicPartitionRosterProtocol.KeySpace,
            AtomicPartitionRosterProtocol.PartitionKeyKind, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey);
    }
}
