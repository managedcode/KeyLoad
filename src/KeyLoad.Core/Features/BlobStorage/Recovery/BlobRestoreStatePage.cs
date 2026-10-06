using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobRestoreStatePage(DatabaseEngine database, BlobRestoreMarker marker)
{
    internal BlobRestoreMarker Process(IAtomicTransaction tx, KeyValueRecord record, bool verify)
    {
        const int RemainingReservationEmptyCount = 0;
        const int KeysEmptyCount = 0;
        const int VersionsSingleItemCount = 1;
        const int UploadsEmptyCount = 0;
        const int VersionsStep = 1;

        var blob = BlobKeys.DecodeScope(record.Key.Span, BlobKeys.StateSpace, BlobKeys.StateComponents);
        var state = BlobRecordReader.Decode<BlobState>(record.Value.Span);
        if (!record.Key.Span.SequenceEqual(BlobKeys.State(blob, state.UploadId)))
        { throw BlobErrors.Corruption(); }
        BlobRecordReader.ValidateState(state, blob, state.UploadId, verify ? marker.TargetIncarnation : marker.SourceIncarnation);
        ValidatePair(tx, state, verify);
        if (verify)
        { return marker; }
        var accounting = new BlobRestoreAccounting(database, marker);
        accounting.AbortReservation(tx, state);
        var rebound = state with
        {
            Incarnation = marker.TargetIncarnation,
            Status = state.Status == BlobUploadStatus.Active ? BlobUploadStatus.Aborted : state.Status,
            RemainingReservation = RemainingReservationEmptyCount
        };
        accounting.Add(tx, blob, rebound.ChargedBytes, KeysEmptyCount, VersionsSingleItemCount, UploadsEmptyCount);
        BlobMetadataRules.Put(tx, record.Key.ToArray(), rebound);
        return marker with { ReservedBytes = checked(marker.ReservedBytes + rebound.ChargedBytes), Versions = checked(marker.Versions + VersionsStep) };
    }

    internal void ValidatePair(IKeyValueView view, BlobState state, bool verify)
    {
        const int EmptyReclaimCursor = 0;
        const int RevisionStep = 1;

        _ = new BlobRestoreAccounting(database, marker).Resource(view, state.Blob);
        var head = BlobRecordReader.Get<BlobHead>(view, BlobKeys.Head(state.Blob)) ?? throw BlobErrors.Corruption();
        BlobRecordReader.ValidateHead(head, state.Blob, verify ? marker.TargetIncarnation : marker.SourceIncarnation);
        var current = head.Metadata.VersionId == state.UploadId;
        if (current != (state.Status == BlobUploadStatus.Complete && !state.Retired))
        { throw BlobErrors.Corruption(); }
        if (!current)
        { return; }
        if (state.ReclaimCursor != EmptyReclaimCursor || state.DeclaredLength != head.Metadata.Length || state.NextOrdinal != head.Metadata.PartCount
            || state.IntegrityHash != head.Metadata.IntegrityHash || state.Access != head.Metadata.Access
            || state.ExpectedRevision != head.Metadata.Revision - RevisionStep)
        { throw BlobErrors.Corruption(); }
    }
}
