using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BackupRestore.Serialization;

internal static class AtomicPartitionRosterKeys
{
    internal static byte[] Partition(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return KeyCodec.Encode(AtomicPartitionRosterProtocol.KeySpace, AtomicPartitionRosterProtocol.KeyVersion,
            AtomicPartitionRosterProtocol.PartitionKeyKind, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey);
    }
}
