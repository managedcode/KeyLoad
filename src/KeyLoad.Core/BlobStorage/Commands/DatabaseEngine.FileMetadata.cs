using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static FileUpload ReserveFile(IAtomicTransaction transaction, PrincipalRecord principal,
        FileMetadataMutation request)
    {
        var pair = ReadBoundResourcePair(transaction, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var key = FileMetadataKeys.Upload(request.Address, pair.Lifetime.Incarnation, request.UploadId);
        var existing = transaction.GetRecord<FileUpload>(key);
        if (existing is not null)
        {
            RequireFileUpload(existing, principal, request, pair.Definition, pair.Lifetime);
            if (existing.Published || existing.Aborted)
            {
                throw Errors.Fail(ErrorCode.Conflict, FileStorageProtocol.Invalid);
            }
            if (existing.Length != request.Length || existing.ContentType != request.ContentType || existing.Access != request.Access)
            {
                throw Errors.Fail(ErrorCode.Conflict, FileStorageProtocol.Invalid);
            }
            return existing;
        }
        var policy = pair.Definition.FilePolicy;
        var quota = ReadFileQuota(transaction, request.Address, pair.Lifetime.Incarnation);
        if (request.UploadId == Guid.Empty || request.Length < FileStorageProtocol.EmptyCount || request.Length > policy.MaximumFileBytes
            || quota.ActiveUploads >= policy.MaximumUploads || quota.Objects >= policy.MaximumObjects
            || request.Length > policy.MaximumStoredBytes - quota.AllocatedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, FileStorageProtocol.Invalid);
        }
        var head = transaction.GetRecord<FileEntry>(FileMetadataKeys.Head(request.Address, pair.Lifetime.Incarnation));
        if ((head is null || head.Deleted) && quota.Files >= policy.MaximumFiles)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, FileStorageProtocol.Invalid);
        }
        var upload = new FileUpload(request.Address, pair.Lifetime.Incarnation, pair.Definition.SchemaVersion,
            request.UploadId, principal.Id, head?.Revision ?? FileStorageProtocol.InitialRevision, request.Length, request.ContentType,
            request.Access, Guid.Empty, false, false, request.TransferOwner);
        transaction.PutRecord(key, upload);
        transaction.PutRecord(FileMetadataKeys.Quota(request.Address, pair.Lifetime.Incarnation), quota with
        {
            AllocatedBytes = checked(quota.AllocatedBytes + request.Length),
            ActiveUploads = checked(quota.ActiveUploads + FileStorageProtocol.FirstRecordCount),
            Objects = checked(quota.Objects + FileStorageProtocol.FirstRecordCount)
        });
        return upload;
    }

    private static object ChangeFileUpload(IAtomicTransaction transaction, PrincipalRecord principal,
        FileMetadataMutation request, DateTime evaluatedAt)
    {
        var pair = ReadBoundResourcePair(transaction, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var key = FileMetadataKeys.Upload(request.Address, pair.Lifetime.Incarnation, request.UploadId);
        var upload = transaction.GetRecord<FileUpload>(key)
            ?? throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Invalid);
        RequireFileUpload(upload, principal, request, pair.Definition, pair.Lifetime);
        return request.Action switch
        {
            FileMetadataAction.Stage => StageFile(transaction, key, upload, request),
            FileMetadataAction.Publish => PublishFile(transaction, key, upload, request, pair.Definition.FilePolicy, evaluatedAt),
            FileMetadataAction.Abort => AbortFile(transaction, key, upload, request),
            FileMetadataAction.Reclaim => ReclaimFile(transaction, key, upload),
            _ => throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid)
        };
    }

    private static FileUpload StageFile(IAtomicTransaction transaction, byte[] key, FileUpload upload, FileMetadataMutation request)
    {
        if (upload.ObjectId != Guid.Empty || upload.Published || upload.Aborted || request.ObjectId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Conflict, FileStorageProtocol.Invalid);
        }
        var staged = upload with { ObjectId = request.ObjectId };
        transaction.PutRecord(key, staged);
        return staged;
    }

    private static void RequireFileUpload(FileUpload upload, PrincipalRecord principal, FileMetadataMutation request,
        ResourceDefinition definition, ResourceLifetime lifetime)
    {
        if (upload.Address != request.Address || upload.Incarnation != lifetime.Incarnation
            || (request.Action is not (FileMetadataAction.Abort or FileMetadataAction.Reclaim)
                && (upload.SchemaVersion != definition.SchemaVersion || upload.TransferOwner != request.TransferOwner)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, FileStorageProtocol.Invalid);
        }
        if (upload.Creator != principal.Id && request.Action != FileMetadataAction.Reclaim && !(principal.ClusterAdministrator
            && request.Action is FileMetadataAction.Abort or FileMetadataAction.Reclaim))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Denied);
        }
    }
}
