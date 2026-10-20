using KeyLoad.Core.Features.BlobStorage;
using ManagedCode.Communication;
using ManagedCode.Storage.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace KeyLoad.Server;

internal sealed partial class FileStorageController
{
    private FileStorageCursor Cursors => HttpContext.RequestServices.GetRequiredService<FileStorageCursor>();

    /// <summary>Reads the current authorized canonical file metadata.</summary>
    [HttpGet(MetadataRoute), FileStorageCapability(Capability.BlobRead)]
    public async Task<Result<BlobMetadata>> MetadataAsync(string path, CancellationToken cancellationToken)
    {
        var state = await Files.ReadAsync(path, Capability.BlobRead, Guid.Empty, cancellationToken);
        var entry = state.Entry is { Deleted: false } actual ? actual
            : throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Invalid);
        return Result<BlobMetadata>.Succeed(FileStorageRequest.PublicMetadata(entry));
    }

    /// <summary>Lists a bounded metadata page after the caller's encrypted scope-bound cursor.</summary>
    [HttpGet(ListRoute), FileStorageCapability(Capability.BlobRead)]
    public async Task<Result<BlobMetadata[]>> ListAsync([FromQuery] string? after, CancellationToken cancellationToken)
    {
        var state = await Files.ReadAsync(string.Empty, Capability.BlobRead, Guid.Empty, cancellationToken, true, Cursors.Decode(Files, after));
        if (state.HasMore)
        {
            Response.Headers[FileStorageHeaders.Next] = Cursors.Encode(Files, state.NextAfter);
        }
        return Result<BlobMetadata[]>.Succeed(state.Entries.Select(FileStorageRequest.PublicMetadata).ToArray());
    }

    /// <summary>Retires visibility atomically; physical versions remain accounted until authorized cleanup.</summary>
    [HttpDelete(FileRoute), FileStorageCapability(Capability.BlobDelete)]
    public async Task<IActionResult> DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        _ = await Files.DeleteAsync(path, cancellationToken);
        return NoContent();
    }

    /// <summary>Reclaims a bounded page of joined retired objects through the configured provider.</summary>
    [HttpPost(CleanupRoute), FileStorageCapability(Capability.BlobManage)]
    public async Task<Result<int>> CleanupAsync([FromQuery] string? after, CancellationToken cancellationToken)
    {
        var page = await Files.ReadAsync(string.Empty, Capability.BlobManage, Guid.Empty, cancellationToken, true, Cursors.Decode(Files, after));
        var reclaimed = FileStorageProtocol.EmptyCount;
        foreach (var upload in page.Uploads)
        {
            var state = await Files.ReadAsync(upload.Address.Path, Capability.BlobManage, upload.UploadId, cancellationToken);
            if ((!upload.Published && !upload.Aborted) || state.Entry is { Deleted: false } head && head.ObjectId == upload.ObjectId)
            {
                continue;
            }
            if (upload.ObjectId != Guid.Empty)
            {
                var deleted = await Storage.DeleteAsync(FilePhysicalIdentity.Object(upload.ObjectId), cancellationToken);
                if (deleted.IsFailed)
                {
                    continue;
                }
            }
            _ = await Files.ReclaimAsync(upload, cancellationToken);
            reclaimed++;
        }
        if (page.HasMore)
        {
            Response.Headers[FileStorageHeaders.Next] = Cursors.Encode(Files, page.NextAfter);
        }
        return Result<int>.Succeed(reclaimed);
    }
}
