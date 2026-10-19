using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeFileMetadata(IKeyValueView view, PrincipalRecord principal, FileMetadataMutation request)
    {
        FileMetadataKeys.Validate(request.Address);
        var pair = ReadBoundResourcePair(view, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var retained = request.UploadId == Guid.Empty ? null
            : view.GetRecord<FileUpload>(FileMetadataKeys.Upload(request.Address, pair.Lifetime.Incarnation, request.UploadId));
        var capability = request.Action switch
        {
            FileMetadataAction.Delete => Capability.BlobDelete,
            FileMetadataAction.Reclaim when retained is { Aborted: true } && retained.Creator == principal.Id => Capability.BlobWrite,
            FileMetadataAction.Reclaim => Capability.BlobManage,
            _ => Capability.BlobWrite
        };
        Authorization.Require(principal, request.Address.Partition, request.Address.Resource, capability);
        if (!pair.Definition.FilePolicy.IsValid() || request.ContentType is null
            || request.ContentType.Length > FileStorageProtocol.MaximumContentTypeCharacters
            || request.ContentType.Any(char.IsControl) || request.Metadata is null
            || request.Metadata.Count > Limits.MaxBatchMutations
            || request.Metadata.Any(pair => pair.Key is null || pair.Value is null)
            || OrleansSerialization.Measure(request.Metadata) > Limits.MaxQueryBytes
            || request.TransferOwner is null || request.TransferOwner.Length > FileStorageProtocol.MaximumContentTypeCharacters
            || request.Access is null || request.Sha256 is null || !Enum.IsDefined(request.Action))
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
        Authorization.RequireWriteRow(principal, request.Access);
        var head = view.GetRecord<FileEntry>(FileMetadataKeys.Head(request.Address, pair.Lifetime.Incarnation));
        if (retained is not null)
        {
            Authorization.RequireWriteRow(principal, retained.Access);
        }
        if (head is not null && request.Action is not (FileMetadataAction.Abort or FileMetadataAction.Reclaim))
        {
            Authorization.RequireWriteRow(principal, head.Access);
        }
    }

    private static FileQuota ReadFileQuota(IKeyValueView view, FileAddress address, ResourceIncarnation incarnation)
    {
        var quota = view.GetRecord<FileQuota>(FileMetadataKeys.Quota(address, incarnation));
        if (quota is null)
        {
            if (view.Scan(FileMetadataKeys.Prefix(address, incarnation, FileStorageProtocol.HeadFamily), FileStorageProtocol.FirstRecordCount).Records.Length != FileStorageProtocol.EmptyCount
                || view.Scan(FileMetadataKeys.Prefix(address, incarnation, FileStorageProtocol.UploadFamily), FileStorageProtocol.FirstRecordCount).Records.Length != FileStorageProtocol.EmptyCount)
            {
                throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
            }
            return new();
        }
        if (quota.AllocatedBytes < FileStorageProtocol.EmptyCount || quota.ActiveUploads < FileStorageProtocol.EmptyCount || quota.Objects < FileStorageProtocol.EmptyCount || quota.Files < FileStorageProtocol.EmptyCount
            || quota.ActiveUploads > quota.Objects || quota.Files > quota.Objects)
        {
            throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
        }
        return quota;
    }

    private static void ValidateFileCachedOutcome(IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        if (operation.Value!.Kind != OperationKind.FileMetadata)
        {
            return;
        }
        var request = Payload<FileMetadataMutation>(operation);
        var pair = ReadBoundResourcePair(view, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var upload = view.GetRecord<FileUpload>(FileMetadataKeys.Upload(request.Address, pair.Lifetime.Incarnation, request.UploadId));
        if (request.Action is FileMetadataAction.Reserve or FileMetadataAction.Stage or FileMetadataAction.Publish)
        {
            if (upload is null || upload.Aborted || request.Action is FileMetadataAction.Stage or FileMetadataAction.Reserve && upload.Published)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, FileStorageProtocol.Invalid);
            }
            RequireFileUpload(upload, principal, request, pair.Definition, pair.Lifetime);
            var head = view.GetRecord<FileEntry>(FileMetadataKeys.Head(request.Address, pair.Lifetime.Incarnation));
            if (request.Action == FileMetadataAction.Publish && (head is null || head.Deleted || head.ObjectId != upload.ObjectId))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, FileStorageProtocol.Invalid);
            }
        }
    }
}
