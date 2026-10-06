using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobReclaimCommand(DatabaseEngine database)
{
    private Guid Incarnation => database.Store.Identity.Incarnation;
    private BlobRecordReader Reader => new(Incarnation);

    internal BlobReclaimResult Execute(IAtomicTransaction tx, ReclaimBlobRequest request, DateTimeOffset now)
    {
        const int MaxPartsFirstCount = 1;
        const int DeletedPartsEmptyCount = 0;
        const int RemainingPartsEmptyCount = 0;
        const int ReleasedBytesEmptyCount = 0;
        const int NoReleasedReservationBytes = 0;
        const int KeysEmptyCount = 0;
        const int VersionsRemovalDelta = -1;
        const int VersionsEmptyCount = 0;
        const int UploadsEmptyCount = 0;

        if (request.MaxParts is < MaxPartsFirstCount or > BlobLimits.MaxReclaimParts)
        { throw BlobErrors.Validation(); }
        var head = Reader.Head(tx, request.Blob);
        var state = Reader.State(tx, request.Blob, request.UploadId);
        if (head?.Metadata.VersionId == request.UploadId)
        {
            if (state is null)
            { throw BlobErrors.Corruption(); }
            _ = Reader.Current(tx, head);
            throw BlobErrors.StateConflict();
        }
        if (state is null)
        {
            ProveEmpty(tx, request.Blob, request.UploadId);
            return new(request.UploadId, DeletedPartsEmptyCount, RemainingPartsEmptyCount, ReleasedBytesEmptyCount, true);
        }
        if (head is null)
        { throw BlobErrors.Corruption(); }
        if (state.Status == BlobUploadStatus.Active && state.ExpiresAt > now
            || state.Status == BlobUploadStatus.Complete && !state.Retired)
        { throw BlobErrors.StateConflict(); }
        var released = state.Status == BlobUploadStatus.Active ? state.RemainingReservation : NoReleasedReservationBytes;
        state = new BlobUploadCommands(database).ReleaseActive(tx, state, BlobUploadStatus.Expired);
        var count = Math.Min(request.MaxParts, state.NextOrdinal - state.ReclaimCursor);
        var deletedBytes = DeleteParts(tx, state, count);
        var cursor = checked(state.ReclaimCursor + count);
        var done = cursor == state.NextOrdinal;
        var policy = database.Resource(tx, request.Blob.Partition, request.Blob.Resource, ResourceKind.BlobStore).BlobPolicy ?? new BlobPolicy();
        BlobQuotaOperations.Change(tx, request.Blob, Incarnation, policy, -deletedBytes, KeysEmptyCount, done ? VersionsRemovalDelta : VersionsEmptyCount, UploadsEmptyCount);
        if (done)
        {
            ProveEmpty(tx, request.Blob, request.UploadId);
            tx.Delete(BlobKeys.State(request.Blob, request.UploadId));
        }
        else
        { BlobMetadataRules.Put(tx, BlobKeys.State(request.Blob, request.UploadId), state with { ReclaimCursor = cursor, StoredBytes = state.StoredBytes - deletedBytes }); }
        return new(request.UploadId, count, state.NextOrdinal - cursor, checked(released + deletedBytes), done);
    }

    private static long DeleteParts(IAtomicTransaction tx, BlobState state, int count)
    {
        const long BytesInitialValue = 0L;

        var bytes = BytesInitialValue;
        for (var ordinal = state.ReclaimCursor; ordinal < state.ReclaimCursor + count; ordinal++)
        {
            var meta = BlobRecordReader.Part(tx, state, ordinal, static _ => { });
            bytes = checked(bytes + meta.Length);
            tx.Delete(BlobKeys.Part(state.Blob, state.UploadId, ordinal));
            tx.Delete(BlobKeys.PartMeta(state.Blob, state.UploadId, ordinal));
        }
        return bytes;
    }

    private static void ProveEmpty(IKeyValueView view, BlobRef blob, Guid upload)
    {
        const int MaxRecordsSingleItemCount = 1;
        const int EmptyRecords = 0;

        var raw = KeySpace.Partition(BlobKeys.PartSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(BlobKeys.GuidFormat));
        var meta = KeySpace.Partition(BlobKeys.PartMetaSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(BlobKeys.GuidFormat));
        if (view.VisitRange(raw, MaxRecordsSingleItemCount, static (_, _) => false).Records != EmptyRecords
            || view.VisitRange(meta, MaxRecordsSingleItemCount, static (_, _) => false).Records != EmptyRecords)
        { throw BlobErrors.Corruption(); }
    }
}
