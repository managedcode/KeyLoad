using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobRestoreHeadPage(DatabaseEngine database, BlobRestoreMarker marker)
{
    internal BlobRestoreMarker Process(IAtomicTransaction tx, KeyValueRecord record, bool verify)
    {
        const int BytesEmptyCount = 0;
        const int KeysSingleItemCount = 1;
        const int VersionsEmptyCount = 0;
        const int UploadsEmptyCount = 0;
        const int ObjectKeysStep = 1;

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
        accounting.Add(tx, blob, BytesEmptyCount, KeysSingleItemCount, VersionsEmptyCount, UploadsEmptyCount);
        BlobMetadataRules.Put(tx, record.Key.ToArray(), head with { Incarnation = marker.TargetIncarnation });
        return marker with { ObjectKeys = checked(marker.ObjectKeys + ObjectKeysStep) };
    }

    private static void ValidateCurrent(BlobMetadata metadata, BlobState state)
    {
        const int EmptyReclaimCursor = 0;
        const int RevisionStep = 1;

        if (state.Status != BlobUploadStatus.Complete || state.Retired || state.ReclaimCursor != EmptyReclaimCursor
            || state.DeclaredLength != metadata.Length || state.NextOrdinal != metadata.PartCount
            || state.IntegrityHash != metadata.IntegrityHash || state.Access != metadata.Access
            || state.ExpectedRevision != metadata.Revision - RevisionStep)
        { throw BlobErrors.Corruption(); }
    }
}
