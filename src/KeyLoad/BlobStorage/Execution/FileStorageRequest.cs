using System.Collections.Immutable;
using KeyLoad.Core.Features.BlobStorage;
using ManagedCode.Storage.Core.Models;
using ManagedCode.Storage.FileSystem;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class FileStorageRequest(IHttpContextAccessor accessor, IFileSystemStorage storage, IOptions<NodeOptions> node,
    IOptions<FileStorageOptions> execution)
{
    private const char CommandSeparator = ':';

    internal TimeSpan CleanupTimeout => execution.Value.CleanupTimeout;

    internal HttpContext Context => accessor.HttpContext ?? throw Errors.Fail(ErrorCode.Unauthenticated, FileStorageProtocol.Invalid);
    internal FileStorageScope Scope => Context.Features.Get<FileStorageScope>()
        ?? throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Invalid);
    internal IFileSystemStorage Storage => storage;
    internal string PrincipalId => Principal.Id;
    internal long PolicyEpoch => Principal.PolicyEpoch;
    private PrincipalRecord Principal => Context.Items[ServerProtocol.PrincipalItem] as PrincipalRecord
        ?? throw Errors.Fail(ErrorCode.Unauthenticated, FileStorageProtocol.Invalid);

    internal FileAddress Address(string path)
    {
        var address = Scope.Address with { Path = path };
        FileMetadataKeys.Validate(address);
        return address;
    }

    internal Guid UploadId(string publicId) => FilePhysicalIdentity.Upload(Scope.PhysicalNamespace, PrincipalId, publicId);

    internal async Task<FileMetadataState> ReadAsync(string path, Capability capability, Guid uploadId, CancellationToken token,
        bool list = false, string? after = null)
    {
        var result = await CanonicalOperationGateway.ExecuteFileMetadataAsync(Context,
            new(Scope.Address with { Path = path }, capability, uploadId, list, after), null, token) as FileMetadataState
            ?? throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
        if (result.Incarnation != Scope.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, FileStorageProtocol.Invalid);
        }
        return result;
    }

    internal Task<FileUpload> ReserveAsync(string path, Guid uploadId, long length, string contentType, CancellationToken token)
        => ChangeAsync<FileUpload>(new(CommandId(uploadId, FileMetadataAction.Reserve, Guid.Empty), Address(path),
            FileMetadataAction.Reserve, uploadId, Guid.Empty, length, contentType, string.Empty, Access(), node.Value.SiloName), token);

    internal async Task<FileUpload> StageAsync(FileUpload upload, CancellationToken token,
        ImmutableDictionary<string, string>? metadata = null)
    {
        var objectId = Guid.NewGuid();
        return await ChangeAsync<FileUpload>(Mutation(upload, FileMetadataAction.Stage, objectId) with
        {
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty
        }, token);
    }

    internal Task<FileEntry> PublishAsync(FileUpload upload, string sha256, ImmutableDictionary<string, string> metadata,
        CancellationToken token)
        => ChangeAsync<FileEntry>(Mutation(upload, FileMetadataAction.Publish, upload.ObjectId) with
        {
            Sha256 = sha256,
            Metadata = metadata
        }, token);

    internal Task<FileUpload> AbortAsync(FileUpload upload, Guid joinedObject, CancellationToken token)
        => ChangeAsync<FileUpload>(Mutation(upload, FileMetadataAction.Abort, joinedObject), token);

    internal Task<bool> ReclaimAsync(FileUpload upload, CancellationToken token)
        => ChangeAsync<bool>(Mutation(upload, FileMetadataAction.Reclaim, upload.ObjectId), token);

    internal Task<FileEntry> DeleteAsync(string path, CancellationToken token)
        => ChangeAsync<FileEntry>(new(Guid.NewGuid(), Address(path), FileMetadataAction.Delete,
            Guid.Empty, Guid.Empty, FileStorageProtocol.EmptyCount, string.Empty, string.Empty, Access(), node.Value.SiloName), token);

    private async Task<T> ChangeAsync<T>(FileMetadataMutation mutation, CancellationToken token)
    {
        var value = await CanonicalOperationGateway.ExecuteFileMetadataAsync(Context, null, mutation, token);
        return value is T actual ? actual : throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
    }

    private FileMetadataMutation Mutation(FileUpload upload, FileMetadataAction action, Guid objectId)
        => new(CommandId(upload.UploadId, action, objectId), upload.Address, action, upload.UploadId, objectId,
            upload.Length, upload.ContentType, string.Empty, upload.Access, node.Value.SiloName);

    private Guid CommandId(Guid upload, FileMetadataAction action, Guid objectId)
        => FilePhysicalIdentity.Upload(Scope.PhysicalNamespace, PrincipalId,
            string.Join(CommandSeparator, upload.ToString(FileStorageProtocol.GuidFormat), action, objectId.ToString(FileStorageProtocol.GuidFormat)));

    private RowAccess Access() => new(Context.Request.Headers[FileStorageHeaders.Owner].FirstOrDefault() ?? Principal.OwnerId,
        Context.Request.Headers[FileStorageHeaders.Project].FirstOrDefault(),
        Context.Request.Headers[FileStorageHeaders.Team].FirstOrDefault());

    internal static BlobMetadata PublicMetadata(FileEntry entry) => new()
    {
        Name = Path.GetFileName(entry.Address.Path),
        FullName = entry.Address.Path,
        CreatedOn = entry.CreatedOn,
        LastModified = entry.ModifiedOn,
        MimeType = entry.ContentType,
        Length = checked((ulong)entry.Length),
        Metadata = entry.Metadata.ToDictionary()
    };
}
