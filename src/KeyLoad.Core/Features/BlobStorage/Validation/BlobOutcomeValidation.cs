using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobOutcomeValidation(DatabaseEngine database)
{
    private BlobRecordReader Reader => new(database.Store.Identity.Incarnation);
    internal BlobOutcomeAuthority? Capture(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        if (operation.Kind == OperationKind.BeginBlobUpload)
        { return new(BlobKeys.FormatVersion, operation.Id, principal.Id); }
        var scope = BlobCommandScope.From(operation);
        var state = scope.UploadId is { } upload ? Reader.State(view, scope.Blob, upload) : null;
        return state is null ? null : new(BlobKeys.FormatVersion, state.BeginCommandId, state.CreatorPrincipalId);
    }

    internal void Validate(IKeyValueView view, ReplicatedOperation operation, OperationResult result, BlobOutcomeAuthority? authority)
    {
        if (authority is not null)
        {
            BlobRecordReader.Version(authority.FormatVersion);
            if (authority.BeginCommandId == Guid.Empty || string.IsNullOrEmpty(authority.CreatorPrincipalId))
            { throw BlobErrors.Corruption(); }
        }
        if (result.Error is not null || operation.Kind == OperationKind.DeleteBlob)
        { return; }
        var scope = BlobCommandScope.From(operation);
        var upload = scope.UploadId ?? throw BlobErrors.Corruption();
        var head = Reader.Head(view, scope.Blob);
        var state = Reader.State(view, scope.Blob, upload);
        if (head?.Metadata.VersionId == upload && state is null)
        { throw BlobErrors.Corruption(); }
        if (state is null && operation.Kind == OperationKind.ReclaimBlob
            && result.Get<BlobCommitResult<BlobReclaimResult>>().Value.Complete)
        { return; }
        if (state is null)
        { throw BlobErrors.Token(); }
        if (authority is null)
        {
            if (operation.Kind == OperationKind.ReclaimBlob)
            { throw BlobErrors.Token(); }
            throw BlobErrors.Corruption();
        }
        if (authority.BeginCommandId != state.BeginCommandId || authority.CreatorPrincipalId != state.CreatorPrincipalId)
        { throw BlobErrors.Token(); }
    }
}
