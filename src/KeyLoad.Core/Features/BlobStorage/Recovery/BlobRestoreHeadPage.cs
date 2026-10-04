using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobRestoreHeadPage(DatabaseEngine database, BlobRestoreMarker marker)
{
    internal BlobRestoreMarker Process(IAtomicTransaction tx, KeyValueRecord record, bool verify)
    {
        var blob = BlobKeys.DecodeScope(record.Key.Span, BlobKeys.HeadSpace, BlobKeys.ScopeComponents);
        var head = BlobRecordReader.Decode<BlobHead>(record.Value.Span);
        BlobRecordReader.ValidateHead(head, blob, verify ? marker.TargetIncarnation : marker.SourceIncarnation);
        var accounting = new BlobRestoreAccounting(database, marker);
        _ = accounting.Resource(tx, blob);
        if (head.Metadata.VersionId is { } upload)
        {
            var state = BlobRecordReader.Get<BlobState>(tx, BlobKeys.State(blob, upload)) ?? throw BlobErrors.Corruption();
            BlobRecordReader.ValidateState(state, blob, upload, marker.TargetIncarnation);
            ValidateCurrent(head.Metadata, state);
        }
        if (verify)
        { return marker; }
        accounting.Add(tx, blob, 0, 1, 0, 0);
        BlobMetadataRules.Put(tx, record.Key.ToArray(), head with { Incarnation = marker.TargetIncarnation });
        return marker with { ObjectKeys = checked(marker.ObjectKeys + 1) };
    }

    private static void ValidateCurrent(BlobMetadata metadata, BlobState state)
    {
        if (state.Status != BlobUploadStatus.Complete || state.Retired || state.ReclaimCursor != 0
            || state.DeclaredLength != metadata.Length || state.NextOrdinal != metadata.PartCount
            || state.IntegrityHash != metadata.IntegrityHash || state.Access != metadata.Access
            || state.ExpectedRevision != metadata.Revision - 1)
        { throw BlobErrors.Corruption(); }
    }
}
