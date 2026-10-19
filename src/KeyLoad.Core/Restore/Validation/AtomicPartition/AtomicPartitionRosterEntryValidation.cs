using KeyLoad.Core.Features.BackupRestore.Serialization;

namespace KeyLoad.Core.Features.BackupRestore.Validation;

internal static class AtomicPartitionRosterEntryValidation
{
    internal static void Validate(AtomicPartitionCatalogEntry? entry, PartitionRef expectedPartition,
        long currentStorePosition, long currentAppliedIndex)
    {
        if (entry is null || entry.Version != AtomicPartitionRosterProtocol.CurrentVersion
            || entry.Partition != expectedPartition || !HasValidFirstSeenPosition(entry, currentStorePosition, currentAppliedIndex))
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.InvalidEntry);
        }
    }

    private static bool HasValidFirstSeenPosition(AtomicPartitionCatalogEntry entry,
        long currentStorePosition, long currentAppliedIndex)
    {
        var localPosition = entry.FirstSeenAppliedIndex == AtomicPartitionRosterProtocol.NoCanonicalSequence
            && entry.FirstSeenStorePosition > AtomicPartitionRosterProtocol.NoStorePosition
            && entry.FirstSeenStorePosition <= currentStorePosition;
        var canonicalPosition = entry.FirstSeenAppliedIndex > AtomicPartitionRosterProtocol.NoCanonicalSequence
            && entry.FirstSeenStorePosition == AtomicPartitionRosterProtocol.NoStorePosition
            && entry.FirstSeenAppliedIndex <= currentAppliedIndex;
        return localPosition || canonicalPosition;
    }
}
