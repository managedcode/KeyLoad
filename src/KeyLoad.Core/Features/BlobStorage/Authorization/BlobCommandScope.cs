using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobCommandScope)]
internal sealed record BlobCommandScope(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobCommandScopeFields.CommandId)] Guid CommandId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobCommandScopeFields.Blob)] BlobRef Blob,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobCommandScopeFields.UploadId)] Guid? UploadId)
{
    internal static T Payload<T>(ReplicatedOperation operation) => NativeCommandPayload.Read<T>(operation);
    internal static BlobCommandScope From(ReplicatedOperation operation) => operation.Kind switch
    {
        OperationKind.BeginBlobUpload => Begin(Payload<BeginBlobUploadRequest>(operation)),
        OperationKind.WriteBlobPart => Write(Payload<WriteBlobPartRequest>(operation)),
        OperationKind.CompleteBlobUpload => Complete(Payload<CompleteBlobUploadRequest>(operation)),
        OperationKind.AbortBlobUpload => Abort(Payload<AbortBlobUploadRequest>(operation)),
        OperationKind.DeleteBlob => Delete(Payload<DeleteBlobRequest>(operation)),
        OperationKind.ReclaimBlob => Reclaim(Payload<ReclaimBlobRequest>(operation)),
        _ => throw BlobErrors.Validation()
    };
    private static BlobCommandScope Begin(BeginBlobUploadRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Write(WriteBlobPartRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Complete(CompleteBlobUploadRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Abort(AbortBlobUploadRequest request) => new(request.CommandId, request.Blob, request.UploadId);
    private static BlobCommandScope Delete(DeleteBlobRequest request) => new(request.CommandId, request.Blob, null);
    private static BlobCommandScope Reclaim(ReclaimBlobRequest request) => new(request.CommandId, request.Blob, request.UploadId);
}

internal sealed class BlobAuthority(DatabaseEngine database)
{
    private BlobRecordReader Reader => new(database.Store.Identity.Incarnation);
    internal ResourceDefinition Scope(IKeyValueView view, PrincipalRecord principal, BlobRef blob, Capability capability)
    {
        BlobKeys.Validate(blob);
        BlobRestoreFence.RequireOpen(view);
        ClusterPrincipalPolicy.RequireOperation(principal, OperationKind.BeginBlobUpload);
        database.Authorization.Require(principal, blob.Partition, blob.Resource, capability);
        var resource = database.Resource(view, blob.Partition, blob.Resource, ResourceKind.BlobStore);
        BlobQuotaOperations.ValidatePolicy(resource);
        var quota = BlobQuotaOperations.Read(view, BlobKeys.Quota(blob), database.Store.Identity.Incarnation);
        var global = BlobQuotaOperations.Read(view, BlobKeys.Global, database.Store.Identity.Incarnation);
        var policy = resource.BlobPolicy ?? new BlobPolicy();
        if (quota.ReservedBytes > global.ReservedBytes || quota.ObjectKeys > global.ObjectKeys
            || quota.Versions > global.Versions || quota.Uploads > global.Uploads
            || quota.ReservedBytes > policy.MaxReservedBytes || quota.ObjectKeys > policy.MaxObjectKeys
            || quota.Versions > policy.MaxVersions || quota.Uploads > policy.MaxUploads)
        { throw BlobErrors.Corruption(); }
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
        { throw Errors.Fail(ErrorCode.PermissionDenied, BlobErrors.Denied); }
    }

    internal void ReadRow(PrincipalRecord principal, RowAccess access)
    {
        if (!database.Authorization.CanReadRow(principal, access))
        { throw Errors.Fail(ErrorCode.PermissionDenied, BlobErrors.Denied); }
    }

    internal void Authorize(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        var scope = BlobCommandScope.From(operation);
        if (scope.CommandId != operation.Id || scope.UploadId == Guid.Empty)
        { throw BlobErrors.Validation(); }
        var capability = operation.Kind switch
        {
            OperationKind.DeleteBlob => Capability.BlobDelete,
            OperationKind.ReclaimBlob => Capability.BlobManage,
            _ => Capability.BlobWrite
        };
        _ = Scope(view, principal, scope.Blob, capability);
        var head = Reader.Head(view, scope.Blob);
        var state = scope.UploadId is { } upload ? Reader.State(view, scope.Blob, upload) : null;
        if (head is null && state is not null)
        { throw BlobErrors.Corruption(); }
        if (head is not null && operation.Kind is OperationKind.BeginBlobUpload
            or OperationKind.CompleteBlobUpload or OperationKind.DeleteBlob)
        { database.Authorization.RequireWriteRow(principal, head.Metadata.Access); }
        if (operation.Kind == OperationKind.BeginBlobUpload)
        {
            if (state is not null)
            { Creator(principal, state); }
            var request = BlobCommandScope.Payload<BeginBlobUploadRequest>(operation);
            var access = request.Access ?? head?.Metadata.Access ?? new RowAccess();
            BlobMetadataRules.Access(access, false);
            database.Authorization.RequireWriteRow(principal, access);
        }
        else if (operation.Kind is not (OperationKind.DeleteBlob or OperationKind.ReclaimBlob) && state is null)
        { throw BlobErrors.Token(); }
        else
        { WriteState(principal, state); }
    }
}
