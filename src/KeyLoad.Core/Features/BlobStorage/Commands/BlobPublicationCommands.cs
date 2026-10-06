using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobPublicationCommands(DatabaseEngine database)
{
    private Guid Incarnation => database.Store.Identity.Incarnation;
    private BlobRecordReader Reader => new(Incarnation);

    internal BlobMetadata Complete(IAtomicTransaction tx, CompleteBlobUploadRequest request, DateTimeOffset now)
    {
        const int BytesEmptyCount = 0;
        const int KeysEmptyCount = 0;
        const int VersionsEmptyCount = 0;
        const int UploadsRemovalDelta = -1;

        var state = Reader.State(tx, request.Blob, request.UploadId) ?? throw BlobErrors.Token();
        BlobUploadCommands.RequireActive(state, now);
        if (state.StoredBytes != state.DeclaredLength || state.NextOrdinal != BlobRecordReader.PartCount(state.DeclaredLength))
        { throw BlobErrors.StateConflict(); }
        if (!BlobIntegrity.Matches(state.IntegrityHash, request.ExpectedIntegrityHash))
        { throw BlobErrors.Validation(); }
        var head = Reader.Head(tx, request.Blob) ?? throw BlobErrors.Corruption();
        if (head.Metadata.Revision != state.ExpectedRevision)
        { throw Errors.Fail(ErrorCode.RevisionConflict, BlobErrors.Revision); }
        var policy = database.Resource(tx, request.Blob.Partition, request.Blob.Resource, ResourceKind.BlobStore).BlobPolicy ?? new BlobPolicy();
        var metadata = new BlobMetadata(request.Blob, NextRevision(head.Metadata.Revision), request.UploadId,
            state.DeclaredLength, state.NextOrdinal, state.IntegrityHash, state.Access, now);
        var headBytes = BlobMetadataRules.Encode(new BlobHead(BlobKeys.FormatVersion, Incarnation, metadata));
        var stateBytes = BlobMetadataRules.Encode(state with { Status = BlobUploadStatus.Complete });
        Retire(tx, head);
        BlobQuotaOperations.Change(tx, request.Blob, Incarnation, policy, BytesEmptyCount, KeysEmptyCount, VersionsEmptyCount, UploadsRemovalDelta);
        tx.Put(BlobKeys.Head(request.Blob), headBytes);
        tx.Put(BlobKeys.State(request.Blob, request.UploadId), stateBytes);
        return metadata;
    }

    internal BlobMetadata Delete(IAtomicTransaction tx, DeleteBlobRequest request, DateTimeOffset now)
    {
        const int ExpectedRevisionValidationBoundary = 0;
        const int LengthEmptyCount = 0;
        const int PartCountEmptyCount = 0;

        if (request.ExpectedRevision <= ExpectedRevisionValidationBoundary)
        { throw BlobErrors.Validation(); }
        var head = Reader.Head(tx, request.Blob);
        if (head is null || head.Metadata.VersionId is null)
        { throw Errors.Fail(ErrorCode.NotFound, BlobErrors.Missing); }
        if (head.Metadata.Revision != request.ExpectedRevision)
        { throw Errors.Fail(ErrorCode.RevisionConflict, BlobErrors.Revision); }
        Retire(tx, head);
        var metadata = head.Metadata with
        {
            Revision = NextRevision(head.Metadata.Revision),
            VersionId = null,
            Length = LengthEmptyCount,
            PartCount = PartCountEmptyCount,
            IntegrityHash = null,
            UpdatedAt = now,
            Deleted = true
        };
        BlobMetadataRules.Put(tx, BlobKeys.Head(request.Blob), new BlobHead(BlobKeys.FormatVersion, Incarnation, metadata));
        return metadata;
    }

    private void Retire(IAtomicTransaction tx, BlobHead head)
    {
        if (head.Metadata.VersionId is not { } upload)
        { return; }
        var prior = Reader.Current(tx, head);
        BlobMetadataRules.Put(tx, BlobKeys.State(head.Metadata.Blob, upload), prior with { Retired = true });
    }

    private static long NextRevision(long revision)
    {
        const int RevisionStep = 1;

        if (revision == long.MaxValue)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, BlobErrors.Capacity); }
        return revision + RevisionStep;
    }
}
