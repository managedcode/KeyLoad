using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobUploadCommands(DatabaseEngine database)
{
    private Guid Incarnation => database.Store.Identity.Incarnation;
    private BlobRecordReader Reader => new(Incarnation);
    private BlobPolicy Policy(IKeyValueView view, BlobRef blob) => database.Resource(view, blob.Partition, blob.Resource, ResourceKind.BlobStore).BlobPolicy ?? new BlobPolicy();

    internal BlobUploadInfo Begin(IAtomicTransaction tx, PrincipalRecord principal, BeginBlobUploadRequest request, DateTimeOffset now)
    {
        const int RequestLengthValidationBoundary = 0;
        const int ExpectedRevisionValidationBoundary = 0;
        const int HeadMetadataRevisionValidationBoundary = 0;
        const int NextOrdinalEmptyCount = 0;
        const int StoredBytesEmptyCount = 0;
        const int ReclaimCursorEmptyCount = 0;
        const int RevisionEmptyCount = 0;
        const int LengthEmptyCount = 0;
        const int PartCountEmptyCount = 0;
        const int KeysSingleItemCount = 1;
        const int KeysEmptyCount = 0;
        const int VersionsSingleItemCount = 1;
        const int UploadsSingleItemCount = 1;

        var policy = Policy(tx, request.Blob);
        if (request.Length < RequestLengthValidationBoundary || request.Length > policy.MaxBlobBytes || request.ExpectedRevision < ExpectedRevisionValidationBoundary)
        { throw BlobErrors.Validation(); }
        var head = Reader.Head(tx, request.Blob);
        if ((head?.Metadata.Revision ?? HeadMetadataRevisionValidationBoundary) != request.ExpectedRevision)
        { throw Errors.Fail(ErrorCode.RevisionConflict, BlobErrors.Revision); }
        if (Reader.State(tx, request.Blob, request.UploadId) is not null)
        { throw BlobErrors.StateConflict(); }
        ProveNoParts(tx, request.Blob, request.UploadId);
        var access = request.Access ?? head?.Metadata.Access ?? new RowAccess();
        BlobMetadataRules.Access(access, false);
        var state = new BlobState(BlobKeys.FormatVersion, Incarnation, Incarnation, request.CommandId,
            principal.Id, request.Blob, request.UploadId, access, request.Length, request.ExpectedRevision,
            now.AddSeconds(policy.UploadTtlSeconds), NextOrdinalEmptyCount, StoredBytesEmptyCount,
            BlobIntegrity.InitialHash(Incarnation, request.Blob, request.UploadId, request.Length),
            BlobUploadStatus.Active, request.Length, ReclaimCursorEmptyCount);
        var stateBytes = BlobMetadataRules.Encode(state);
        var headBytes = head is null ? BlobMetadataRules.Encode(new BlobHead(BlobKeys.FormatVersion, Incarnation,
            new(request.Blob, RevisionEmptyCount, null, LengthEmptyCount, PartCountEmptyCount, null, access, now))) : null;
        BlobQuotaOperations.Change(tx, request.Blob, Incarnation, policy, request.Length, head is null ? KeysSingleItemCount : KeysEmptyCount, VersionsSingleItemCount, UploadsSingleItemCount);
        if (headBytes is not null)
        { tx.Put(BlobKeys.Head(request.Blob), headBytes); }
        tx.Put(BlobKeys.State(request.Blob, request.UploadId), stateBytes);
        return state.Info;
    }

    private static void ProveNoParts(IKeyValueView view, BlobRef blob, Guid upload)
    {
        const int MaxRecordsSingleItemCount = 1;
        const int EmptyRecords = 0;

        var raw = KeySpace.Partition(BlobKeys.PartSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(BlobKeys.GuidFormat));
        var meta = KeySpace.Partition(BlobKeys.PartMetaSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(BlobKeys.GuidFormat));
        if (view.VisitRange(raw, MaxRecordsSingleItemCount, static (_, _) => false).Records != EmptyRecords
            || view.VisitRange(meta, MaxRecordsSingleItemCount, static (_, _) => false).Records != EmptyRecords)
        { throw BlobErrors.Corruption(); }
    }

    internal BlobUploadInfo Write(IAtomicTransaction tx, WriteBlobPartRequest request, DateTimeOffset now)
    {
        const int OrdinalValidationBoundary = 0;
        const int ExpectedValidationBoundary = 1;
        const int NextOrdinalStep = 1;

        var state = Reader.State(tx, request.Blob, request.UploadId) ?? throw BlobErrors.Token();
        if (request.Ordinal < OrdinalValidationBoundary || request.Ordinal > state.NextOrdinal || request.Bytes.IsEmpty
            || request.Bytes.Length > BlobLimits.RawPartBytes)
        { throw BlobErrors.Validation(); }
        if (!BlobIntegrity.Matches(BlobIntegrity.PartHash(request.Bytes.Span), request.Sha256))
        { throw BlobErrors.Validation(); }
        if (request.Ordinal < state.NextOrdinal)
        {
            if (request.Ordinal < state.ReclaimCursor)
            { throw BlobErrors.StateConflict(); }
            return Duplicate(tx, state, request);
        }
        RequireActive(state, now);
        var expected = Math.Min(BlobLimits.RawPartBytes, state.DeclaredLength - state.StoredBytes);
        if (request.Bytes.Length != expected || expected < ExpectedValidationBoundary)
        { throw BlobErrors.Validation(); }
        if (tx.ReadOwnedValue(BlobKeys.Part(request.Blob, request.UploadId, request.Ordinal)) is not null
            || tx.ReadOwnedValue(BlobKeys.PartMeta(request.Blob, request.UploadId, request.Ordinal)) is not null)
        { throw BlobErrors.Corruption(); }
        var advanced = state with
        {
            NextOrdinal = checked(state.NextOrdinal + NextOrdinalStep),
            StoredBytes = checked(state.StoredBytes + request.Bytes.Length),
            RemainingReservation = state.RemainingReservation - request.Bytes.Length,
            IntegrityHash = BlobIntegrity.NextHash(state.IntegrityHash, request.Ordinal, request.Bytes.Length, request.Sha256)
        };
        var stateBytes = BlobMetadataRules.Encode(advanced);
        tx.Put(BlobKeys.Part(request.Blob, request.UploadId, request.Ordinal), request.Bytes.ToArray());
        tx.PutRecord(BlobKeys.PartMeta(request.Blob, request.UploadId, request.Ordinal),
            new BlobPartMetadata(BlobKeys.FormatVersion, request.Bytes.Length, request.Sha256));
        tx.Put(BlobKeys.State(request.Blob, request.UploadId), stateBytes);
        return advanced.Info;
    }

    private static BlobUploadInfo Duplicate(IKeyValueView view, BlobState state, WriteBlobPartRequest request)
    {
        var identical = false;
        var meta = BlobRecordReader.Part(view, state, request.Ordinal, bytes => identical = bytes.SequenceEqual(request.Bytes.Span));
        if (!identical || meta.Length != request.Bytes.Length || meta.Sha256 != request.Sha256)
        { throw BlobErrors.StateConflict(); }
        return state.Info;
    }

    internal BlobUploadInfo Abort(IAtomicTransaction tx, AbortBlobUploadRequest request)
    {
        var state = Reader.State(tx, request.Blob, request.UploadId) ?? throw BlobErrors.Token();
        if (state.Status is BlobUploadStatus.Aborted or BlobUploadStatus.Expired)
        { return state.Info; }
        if (state.Status != BlobUploadStatus.Active)
        { throw BlobErrors.StateConflict(); }
        var aborted = ReleaseActive(tx, state, BlobUploadStatus.Aborted);
        BlobMetadataRules.Put(tx, BlobKeys.State(request.Blob, request.UploadId), aborted);
        return aborted.Info;
    }

    internal BlobState ReleaseActive(IAtomicTransaction tx, BlobState state, BlobUploadStatus status)
    {
        const int KeysEmptyCount = 0;
        const int VersionsEmptyCount = 0;
        const int UploadsRemovalDelta = -1;
        const int RemainingReservationEmptyCount = 0;

        if (state.Status != BlobUploadStatus.Active)
        { return state; }
        BlobQuotaOperations.Change(tx, state.Blob, Incarnation, Policy(tx, state.Blob), -state.RemainingReservation, KeysEmptyCount, VersionsEmptyCount, UploadsRemovalDelta);
        return state with { Status = status, RemainingReservation = RemainingReservationEmptyCount };
    }

    internal static void RequireActive(BlobState state, DateTimeOffset now)
    {
        if (state.Status != BlobUploadStatus.Active || state.ExpiresAt <= now)
        { throw BlobErrors.StateConflict(); }
    }
}
