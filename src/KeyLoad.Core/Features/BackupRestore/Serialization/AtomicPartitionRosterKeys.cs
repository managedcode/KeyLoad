namespace KeyLoad.Core.Features.BackupRestore.Serialization;

internal static class AtomicPartitionRosterKeys
{
    internal static byte[] Partition(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return AtomicPartitionRosterRestoreOriginSerialization.EntryKey(partition);
    }
}
