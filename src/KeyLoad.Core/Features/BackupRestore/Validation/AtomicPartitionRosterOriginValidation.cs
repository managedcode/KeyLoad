using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BackupRestore.Validation;

internal static class AtomicPartitionRosterOriginValidation
{
    private const long NoHistoricalBound = 0;
    private const string InvalidOrigin = "The historical partition roster origin is malformed.";

    internal static long ReadBound(IKeyValueView view, AtomicPartitionCatalogEntryV1 entry,
        byte[] originalBytes, Guid currentIncarnation)
    {
        var origin = view.GetRecord<AtomicPartitionRosterRestoreOrigin>(
            AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition));
        if (origin is null)
        { return NoHistoricalBound; }
        var identity = view.GetRecord<AtomicPartitionRosterRestoreIdentity>(
            AtomicPartitionRosterRestoreOriginSerialization.IdentityKey());
        if (identity is null || !AtomicPartitionRosterRestoreOriginSerialization.MatchesIdentity(identity, currentIncarnation))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOrigin); }
        if (entry.FirstSeenAppliedIndex <= NoHistoricalBound
            || !AtomicPartitionRosterRestoreOriginSerialization.Matches(origin, entry.Partition,
                currentIncarnation, originalBytes, entry.FirstSeenAppliedIndex, identity.SourceIncarnation))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOrigin); }
        return origin.AppliedUpperBound;
    }
}
