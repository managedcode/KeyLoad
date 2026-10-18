using ManagedCode.Communication.Commands;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobAuthority(DatabaseEngine database)
{
    private BlobRecordReader Reader => new(database.Store.Identity.Incarnation);
    internal ResourceDefinition Scope(IKeyValueView view, PrincipalRecord principal, BlobRef blob, Capability capability, bool read = false)
    {
        BlobKeys.Validate(blob);
        BlobRestoreFence.RequireOpen(view);
        global::KeyLoad.Core.Features.ClusterRouting.Identity.RuntimeJournalIdentity.RequireOperation(principal, OperationKind.BeginBlobUpload);
        database.Authorization.Require(principal, blob.Partition, blob.Resource, capability);
        var resource = read
            ? DatabaseEngine.BindAuthorizedReadResource(view, blob.Partition, blob.Resource, ResourceKind.BlobStore)
            : database.Resource(view, blob.Partition, blob.Resource, ResourceKind.BlobStore);
        BlobQuotaOperations.ValidatePolicy(resource);
        var quota = BlobQuotaOperations.Read(view, BlobKeys.Quota(blob), database.Store.Identity.Incarnation);
        var global = BlobQuotaOperations.Read(view, BlobKeys.Global, database.Store.Identity.Incarnation);
        var policy = resource.BlobPolicy ?? new BlobPolicy();
        if (quota.ReservedBytes > global.ReservedBytes || quota.ObjectKeys > global.ObjectKeys
                                                       || quota.Versions > global.Versions || quota.Uploads > global.Uploads
                                                       || quota.ReservedBytes > policy.MaxReservedBytes || quota.ObjectKeys > policy.MaxObjectKeys
                                                       || quota.Versions > policy.MaxVersions || quota.Uploads > policy.MaxUploads)
        {
            throw BlobErrors.Corruption();
        }
        return resource;
    }

    internal void WriteState(PrincipalRecord principal, BlobState? state)
    {
        if (state is not null)
        {
            Creator(principal, state);
            database.Authorization.RequireWriteRow(principal, state.Access);
        }
    }

    internal static void Creator(PrincipalRecord principal, BlobState state)
    {
        if (!principal.ClusterAdministrator && principal.Id != state.CreatorPrincipalId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, BlobErrors.Denied);
        }
    }

    internal void ReadRow(PrincipalRecord principal, RowAccess access)
    {
        if (!database.Authorization.CanReadRow(principal, access))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, BlobErrors.Denied);
        }
    }

    internal void Authorize(IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var scope = BlobCommandScope.From(operation);
        if (scope.CommandId != operation.CommandId || scope.UploadId == Guid.Empty)
        {
            throw BlobErrors.Validation();
        }
        var capability = BlobCommandScope.RequiredCapability(operation.Value!.Kind);
        _ = Scope(view, principal, scope.Blob, capability);
        var head = Reader.Head(view, scope.Blob);
        var state = scope.UploadId is { } upload ? Reader.State(view, scope.Blob, upload) : null;
        if (head is null && state is not null)
        {
            throw BlobErrors.Corruption();
        }
        if (head is not null && operation.Value!.Kind is OperationKind.BeginBlobUpload
                or OperationKind.CompleteBlobUpload or OperationKind.DeleteBlob)
        {
            database.Authorization.RequireWriteRow(principal, head.Metadata.Access);
        }
        if (operation.Value!.Kind == OperationKind.BeginBlobUpload)
        {
            if (state is not null)
            {
                Creator(principal, state);
            }
            var request = BlobCommandScope.Payload<BeginBlobUploadRequest>(operation);
            var access = request.Access ?? head?.Metadata.Access ?? new RowAccess();
            BlobMetadataRules.Access(access, false);
            database.Authorization.RequireWriteRow(principal, access);
        }
        else if (operation.Value!.Kind is not (OperationKind.DeleteBlob or OperationKind.ReclaimBlob) && state is null)
        {
            throw BlobErrors.Token();
        }
        else
        {
            WriteState(principal, state);
        }
    }
}
