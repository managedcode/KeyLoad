using KeyLoad.Core.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes FilePublication operations against the caller's database context.</summary>
public static class FilePublication
{
    internal static FileEntry PublishFile(IAtomicTransaction transaction, byte[] key, FileUpload upload, FileMetadataMutation request, FileStoragePolicy policy, DateTime evaluatedAt)
    {
        if (upload.Aborted || upload.Published || upload.ObjectId == Guid.Empty || upload.ObjectId != request.ObjectId || request.Length != upload.Length || request.Sha256.Length != FileStorageProtocol.Sha256Characters || !request.Sha256.All(char.IsAsciiHexDigitLower))
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }

        var headKey = FileMetadataKeys.Head(upload.Address, upload.Incarnation);
        var previous = transaction.GetRecord<FileEntry>(headKey);
        if ((previous?.Revision ?? FileStorageProtocol.InitialRevision) != upload.ExpectedRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, FileStorageProtocol.Invalid);
        }

        var quota = FileAuthorization.ReadFileQuota(transaction, upload.Address, upload.Incarnation);
        var count = previous is null || previous.Deleted ? checked(quota.Files + FileStorageProtocol.FirstRecordCount) : quota.Files;
        if (count > policy.MaximumFiles)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, FileStorageProtocol.Invalid);
        }

        var entry = new FileEntry(upload.Address, upload.Incarnation, upload.ObjectId, checked(upload.ExpectedRevision + FileStorageProtocol.RevisionStep), upload.Length, upload.ContentType, request.Sha256, upload.Access, false)
        {
            Metadata = request.Metadata,
            CreatedOn = previous?.CreatedOn ?? new DateTimeOffset(evaluatedAt),
            ModifiedOn = new DateTimeOffset(evaluatedAt)
        };
        transaction.PutRecord(headKey, entry);
        transaction.PutRecord(key, upload with { Published = true });
        transaction.PutRecord(FileMetadataKeys.Quota(upload.Address, upload.Incarnation), quota with { ActiveUploads = checked(quota.ActiveUploads - FileStorageProtocol.FirstRecordCount), Files = count });
        return entry;
    }

    internal static FileEntry DeleteFile(IAtomicTransaction transaction, FileMetadataMutation request, DateTime evaluatedAt)
    {
        var pair = ResourceDefinitionBinding.ReadBoundResourcePair(transaction, request.Address.Partition, request.Address.Resource, ResourceKind.BlobStore);
        var key = FileMetadataKeys.Head(request.Address, pair.Lifetime.Incarnation);
        var previous = transaction.GetRecord<FileEntry>(key) ?? throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Invalid);
        if (previous.Deleted)
        {
            return previous;
        }

        var deleted = previous with
        {
            Deleted = true,
            Revision = checked(previous.Revision + FileStorageProtocol.RevisionStep),
            ModifiedOn = new DateTimeOffset(evaluatedAt)
        };
        transaction.PutRecord(key, deleted);
        var quota = FileAuthorization.ReadFileQuota(transaction, request.Address, pair.Lifetime.Incarnation);
        transaction.PutRecord(FileMetadataKeys.Quota(request.Address, pair.Lifetime.Incarnation), quota with { Files = checked(quota.Files - FileStorageProtocol.FirstRecordCount) });
        return deleted;
    }

    internal static FileUpload AbortFile(IAtomicTransaction transaction, byte[] key, FileUpload upload, FileMetadataMutation request)
    {
        if (upload.Published || upload.ObjectId != Guid.Empty && upload.ObjectId != request.ObjectId)
        {
            throw Errors.Fail(ErrorCode.Conflict, FileStorageProtocol.Invalid);
        }

        if (!upload.Aborted)
        {
            var quota = FileAuthorization.ReadFileQuota(transaction, upload.Address, upload.Incarnation);
            transaction.PutRecord(FileMetadataKeys.Quota(upload.Address, upload.Incarnation), quota with { ActiveUploads = checked(quota.ActiveUploads - FileStorageProtocol.FirstRecordCount) });
        }

        var aborted = upload with
        {
            Aborted = true
        };
        transaction.PutRecord(key, aborted);
        return aborted;
    }

    internal static bool ReclaimFile(IAtomicTransaction transaction, byte[] key, FileUpload upload)
    {
        var head = transaction.GetRecord<FileEntry>(FileMetadataKeys.Head(upload.Address, upload.Incarnation));
        RequireFileReclaim(upload, head);
        var quota = FileAuthorization.ReadFileQuota(transaction, upload.Address, upload.Incarnation);
        transaction.PutRecord(FileMetadataKeys.Quota(upload.Address, upload.Incarnation), quota with { Objects = checked(quota.Objects - FileStorageProtocol.FirstRecordCount), AllocatedBytes = checked(quota.AllocatedBytes - upload.Length) });
        transaction.Delete(key);
        return true;
    }

    internal static void RequireFileReclaim(FileUpload upload, FileEntry? head)
    {
        if (!upload.Aborted && !upload.Published || head is { Deleted: false } && head.ObjectId == upload.ObjectId)
        {
            throw Errors.Fail(ErrorCode.Conflict, FileStorageProtocol.Invalid);
        }
    }
}
