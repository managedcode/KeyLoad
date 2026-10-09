using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Stages all historical origins inside the genuine operation-owned reset transaction.</summary>
internal static class ZoneTreeRestoreSlotOrigins
{
    private const int SingleRow = 1;
    private const int FirstRecord = 0;
    private const long NoAppliedIndex = 0;
    private const string InvalidOrigin = "The retained restore slot historical origin is inconsistent.";

    internal static void Stage(IAtomicTransaction transaction, ClusterRestoreSlotContext context,
        long originalAppliedIndex, CancellationToken cancellationToken)
    {
        var priorIdentity = transaction.GetRecord<AtomicPartitionRosterRestoreIdentity>(
            AtomicPartitionRosterRestoreOriginSerialization.IdentityKey());
        if (priorIdentity is not null && !AtomicPartitionRosterRestoreOriginSerialization.MatchesIdentity(
            priorIdentity, context.SourceIncarnation))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOrigin); }
        if (context.SourceIncarnation == context.TargetIncarnation || originalAppliedIndex < NoAppliedIndex)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOrigin); }
        var hasOrigins = false;
        byte[]? after = null;
        var prefix = AtomicPartitionRosterRestoreOriginSerialization.EntryPrefix();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = transaction.Scan(prefix, SingleRow, after);
            if (page.Records.IsEmpty)
            { break; }
            var row = page.Records[FirstRecord];
            var entry = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(row.Value.Span);
            if (entry is null || entry.Partition is null)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidOrigin); }
            var origin = transaction.GetRecord<AtomicPartitionRosterRestoreOrigin>(
                AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition));
            ZoneTreeBackupRestoreRoster.RequireEntry(row, entry, origin, priorIdentity,
                context.SourceIncarnation, context.SourcePosition, originalAppliedIndex);
            if (entry.FirstSeenAppliedIndex > NoAppliedIndex)
            {
                var bound = Math.Max(originalAppliedIndex, origin?.AppliedUpperBound ?? NoAppliedIndex);
                transaction.PutRecord(AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition),
                    new AtomicPartitionRosterRestoreOrigin(AtomicPartitionRosterRestoreOriginSerialization.CurrentVersion,
                        entry.Partition, context.SourceIncarnation, context.TargetIncarnation, bound,
                        SHA256.HashData(row.Value.Span)));
                hasOrigins = true;
            }
            after = row.Key.ToArray();
            if (!page.HasMore)
            { break; }
        }
        var actualOrigins = ZoneTreeBackupRestoreRoster.RequireEveryOriginBound(transaction,
            context.SourceIncarnation, context.TargetIncarnation);
        if (actualOrigins != hasOrigins || priorIdentity is not null && !actualOrigins)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOrigin); }
        if (actualOrigins)
        {
            transaction.PutRecord(AtomicPartitionRosterRestoreOriginSerialization.IdentityKey(),
                new AtomicPartitionRosterRestoreIdentity(AtomicPartitionRosterRestoreOriginSerialization.CurrentVersion,
                    context.SourceIncarnation, context.TargetIncarnation));
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
