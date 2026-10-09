using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Carries exact historical roster identity inside an unpublished native restore.</summary>
internal static class ZoneTreeBackupRestoreRoster
{
    private const int SingleRow = 1;
    private const int FirstRecord = 0;
    private const long NoAppliedIndex = 0;
    private const long NoStorePosition = 0;
    private const string InvalidRoster = "The restored historical partition roster is malformed.";

    internal static bool CarryOrigins(ZoneTreeStore restored, Guid sourceIncarnation)
    {
        var sourcePosition = restored.Position;
        var priorIdentity = restored.Read(view => view.GetRecord<AtomicPartitionRosterRestoreIdentity>(
            AtomicPartitionRosterRestoreOriginSerialization.IdentityKey()));
        if (priorIdentity is not null
            && !AtomicPartitionRosterRestoreOriginSerialization.MatchesIdentity(priorIdentity, sourceIncarnation))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
        var sourceApplied = restored.Read(view => view.ReadOwnedValue(KeyCodec.Encode(
            ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey)));
        var applied = sourceApplied is null ? NoAppliedIndex : NativeSerialization.Deserialize<long>(sourceApplied);
        if (applied < NoAppliedIndex) { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
        byte[]? after = null;
        var prefix = AtomicPartitionRosterRestoreOriginSerialization.EntryPrefix();
        while (true)
        {
            var page = restored.Read(view => view.Scan(prefix, SingleRow, after));
            if (page.Records.IsEmpty) { break; }
            var row = page.Records[FirstRecord];
            var entry = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(row.Value.Span);
            if (entry is null || entry.Partition is null)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
            var origin = restored.Read(view => view.GetRecord<AtomicPartitionRosterRestoreOrigin>(
                AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition)));
            RequireEntry(row, entry, origin, priorIdentity, sourceIncarnation, sourcePosition, applied);
            if (entry.FirstSeenAppliedIndex > NoAppliedIndex)
            {
                if (sourceIncarnation == restored.Identity.Incarnation)
                { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
                var upperBound = Math.Max(applied, origin?.AppliedUpperBound ?? NoAppliedIndex);
                var carried = new AtomicPartitionRosterRestoreOrigin(
                    AtomicPartitionRosterRestoreOriginSerialization.CurrentVersion, entry.Partition,
                    sourceIncarnation, restored.Identity.Incarnation, upperBound, SHA256.HashData(row.Value.Span));
                restored.Commit((transaction, _) =>
                {
                    transaction.PutRecord(AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition), carried);
                    return true;
                });
            }
            after = row.Key.ToArray();
            if (!page.HasMore) { break; }
        }
        var hasOrigins = RequireEveryOriginBound(restored, sourceIncarnation);
        if (priorIdentity is not null && !hasOrigins) { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
        return hasOrigins;
    }

    private static bool RequireEveryOriginBound(ZoneTreeStore restored, Guid sourceIncarnation)
    {
        byte[]? after = null;
        var prefix = AtomicPartitionRosterRestoreOriginSerialization.OriginPrefix();
        var hasOrigins = false;
        while (true)
        {
            var page = restored.Read(view => view.Scan(prefix, SingleRow, after));
            if (page.Records.IsEmpty) { return hasOrigins; }
            var row = page.Records[FirstRecord];
            var origin = NativeSerialization.Deserialize<AtomicPartitionRosterRestoreOrigin>(row.Value.Span);
            if (origin is null || origin.Partition is null
                || !row.Key.Span.SequenceEqual(AtomicPartitionRosterRestoreOriginSerialization.OriginKey(origin.Partition)))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
            var original = restored.Read(view => view.ReadOwnedValue(
                AtomicPartitionRosterRestoreOriginSerialization.EntryKey(origin.Partition)));
            if (original is null) { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
            var entry = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(original);
            if (entry is null || entry.Partition != origin.Partition
                || !AtomicPartitionRosterRestoreOriginSerialization.Matches(origin, origin.Partition,
                    restored.Identity.Incarnation, original, entry.FirstSeenAppliedIndex, sourceIncarnation))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
            hasOrigins = true;
            after = row.Key.ToArray();
            if (!page.HasMore) { return hasOrigins; }
        }
    }

    private static void RequireEntry(KeyValueRecord row, AtomicPartitionCatalogEntryV1 entry,
        AtomicPartitionRosterRestoreOrigin? origin, AtomicPartitionRosterRestoreIdentity? priorIdentity, Guid sourceIncarnation, long sourcePosition, long applied)
    {
        if (entry.Version != AtomicPartitionRosterRestoreOriginSerialization.EntryVersion || entry.Partition is null
            || !row.Key.Span.SequenceEqual(AtomicPartitionRosterRestoreOriginSerialization.EntryKey(entry.Partition)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
        if (origin is not null && (priorIdentity is null || !AtomicPartitionRosterRestoreOriginSerialization.Matches(
            origin, entry.Partition, sourceIncarnation, row.Value.Span, entry.FirstSeenAppliedIndex, priorIdentity.SourceIncarnation)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
        var local = entry.FirstSeenAppliedIndex == NoAppliedIndex && origin is null
            && entry.FirstSeenStorePosition > NoStorePosition && entry.FirstSeenStorePosition <= sourcePosition;
        var replicated = entry.FirstSeenStorePosition == NoStorePosition && entry.FirstSeenAppliedIndex > NoAppliedIndex
            && entry.FirstSeenAppliedIndex <= Math.Max(applied, origin?.AppliedUpperBound ?? NoAppliedIndex);
        if (!local && !replicated) { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
    }
}
