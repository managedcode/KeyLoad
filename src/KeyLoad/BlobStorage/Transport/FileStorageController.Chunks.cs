using System.Collections.Immutable;
using KeyLoad.Core.BlobStorage;
using ManagedCode.Communication;
using ManagedCode.Storage.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace KeyLoad.Server;

internal sealed partial class FileStorageController
{
    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobWrite)]
    public override async Task<Result> UploadChunkAsync([FromForm(Name = FileStorageProtocol.EmptyPath)] FileUploadPayload payload, CancellationToken cancellationToken)
    {
        FileChunkAdmission.Validate(payload, serverOptions);
        var descriptor = payload.Payload;
        var path = descriptor.FileName ?? payload.File.FileName;
        var uploadId = Files.UploadId(descriptor.UploadId);
        _ = await Files.ReserveAsync(path, uploadId, descriptor.FileSize!.Value,
            FileStorageNames.ContentType(path, descriptor.ContentType), cancellationToken);
        descriptor.UploadId = uploadId.ToString(FileStorageProtocol.GuidFormat);
        return await base.UploadChunkAsync(payload, cancellationToken);
    }

    /// <inheritdoc />
    [FileStorageCapability(Capability.BlobWrite)]
    public override async Task<Result<ChunkUploadCompleteResponse>> CompleteChunksAsync([FromBody] ChunkUploadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uploadId = Files.UploadId(request.UploadId);
        var state = await Files.ReadAsync(string.Empty, Capability.BlobWrite, uploadId, cancellationToken);
        var reserved = state.Upload ?? throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Invalid);
        if (!request.CommitToStorage || request.KeepMergedFile
            || request.FileName is not null && FileStorageNames.Path(request.FileName, request.Directory) != reserved.Address.Path
            || request.ContentType is not null && !string.Equals(request.ContentType, reserved.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw Errors.Fail(ErrorCode.Validation, FileStorageProtocol.Invalid);
        }
        var checksum = default(uint);
        var metadata = await FileUploadPublication.ExecuteAsync(Files, reserved, async (staged, token) =>
        {
            var complete = new ChunkUploadCompleteRequest
            {
                UploadId = uploadId.ToString(FileStorageProtocol.GuidFormat),
                FileName = FilePhysicalIdentity.Object(staged.ObjectId),
                ContentType = staged.ContentType,
                Metadata = request.Metadata,
                CommitToStorage = true,
                KeepMergedFile = false
            };
            var result = await base.CompleteChunksAsync(complete, token);
            if (result.IsFailed || result.Value?.Metadata is null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, FileStorageProtocol.Invalid);
            }
            checksum = result.Value.Checksum;
            return result.Value.Metadata;
        }, cancellationToken, request.Metadata?.ToImmutableDictionary(StringComparer.Ordinal));
        return Result<ChunkUploadCompleteResponse>.Succeed(new ChunkUploadCompleteResponse { Checksum = checksum, Metadata = metadata });
    }

    /// <inheritdoc />
    [NonAction]
    public override IActionResult AbortChunks(string uploadId)
        => throw Errors.Fail(ErrorCode.PermissionDenied, AsyncAbortRequired);

    /// <summary>Authorizes and joins the library abort before releasing the metadata reservation.</summary>
    [HttpDelete(ChunkRoute), FileStorageCapability(Capability.BlobWrite)]
    public async Task<IActionResult> AbortUploadAsync(string uploadId, CancellationToken cancellationToken)
    {
        var id = Files.UploadId(uploadId);
        var state = await Files.ReadAsync(string.Empty, Capability.BlobWrite, id, cancellationToken);
        var upload = state.Upload ?? throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Invalid);
        var aborted = await Files.AbortAsync(upload, Guid.Empty, cancellationToken);
        _ = base.AbortChunks(id.ToString(FileStorageProtocol.GuidFormat));
        _ = await Files.ReclaimAsync(aborted, cancellationToken);
        return NoContent();
    }
}
