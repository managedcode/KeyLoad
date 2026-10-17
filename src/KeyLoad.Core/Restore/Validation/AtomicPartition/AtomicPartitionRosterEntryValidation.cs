using KeyLoad.Core.Features.BackupRestore.Serialization;

namespace KeyLoad.Core.Features.BackupRestore.Validation;

internal static class AtomicPartitionRosterEntryValidation
{
    internal static void Validate(AtomicPartitionCatalogEntryV1? entry, PartitionRef expectedPartition,
        long currentStorePosition, long currentAppliedIndex)
    {
        if (entry is null || entry.Version != AtomicPartitionRosterProtocol.CurrentVersion
            || entry.Partition != expectedPartition || !HasValidFirstSeenPosition(entry, currentStorePosition, currentAppliedIndex))
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.InvalidEntry);
        }
    }

    private static bool HasValidFirstSeenPosition(AtomicPartitionCatalogEntryV1 entry,
        long currentStorePosition, long currentAppliedIndex)
    {
        var localPosition = entry.FirstSeenAppliedIndex == AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex
            && entry.FirstSeenStorePosition > AtomicPartitionRosterProtocol.NoReplicatedStorePosition
            && entry.FirstSeenStorePosition <= currentStorePosition;
        var replicatedPosition = entry.FirstSeenAppliedIndex > AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex
            && entry.FirstSeenStorePosition == AtomicPartitionRosterProtocol.NoReplicatedStorePosition
            && entry.FirstSeenAppliedIndex <= currentAppliedIndex;
        return localPosition || replicatedPosition;
    }
}
