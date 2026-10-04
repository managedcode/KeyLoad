using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobReclaimCommand(DatabaseEngine database)
{
    private Guid Incarnation => database.Store.Identity.Incarnation;
    private BlobRecordReader Reader => new(Incarnation);

    internal BlobReclaimResult Execute(IAtomicTransaction tx, ReclaimBlobRequest request, DateTimeOffset now)
    {
        if (request.MaxParts is < 1 or > BlobLimits.MaxReclaimParts)
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
            return new(request.UploadId, 0, 0, 0, true);
        }
        if (head is null)
        { throw BlobErrors.Corruption(); }
        if (state.Status == BlobUploadStatus.Active && state.ExpiresAt > now
            || state.Status == BlobUploadStatus.Complete && !state.Retired)
        { throw BlobErrors.StateConflict(); }
        var released = state.Status == BlobUploadStatus.Active ? state.RemainingReservation : 0;
        state = new BlobUploadCommands(database).ReleaseActive(tx, state, BlobUploadStatus.Expired);
        var count = Math.Min(request.MaxParts, state.NextOrdinal - state.ReclaimCursor);
        var deletedBytes = DeleteParts(tx, state, count);
        var cursor = checked(state.ReclaimCursor + count);
        var done = cursor == state.NextOrdinal;
        var policy = database.Resource(tx, request.Blob.Partition, request.Blob.Resource, ResourceKind.BlobStore).BlobPolicy ?? new BlobPolicy();
        BlobQuotaOperations.Change(tx, request.Blob, Incarnation, policy, -deletedBytes, 0, done ? -1 : 0, 0);
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
        var bytes = 0L;
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
        var raw = KeySpace.Partition(BlobKeys.PartSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(BlobKeys.GuidFormat));
        var meta = KeySpace.Partition(BlobKeys.PartMetaSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(BlobKeys.GuidFormat));
        if (view.VisitRange(raw, 1, static (_, _) => false).Records != 0
            || view.VisitRange(meta, 1, static (_, _) => false).Records != 0)
        { throw BlobErrors.Corruption(); }
    }
}
