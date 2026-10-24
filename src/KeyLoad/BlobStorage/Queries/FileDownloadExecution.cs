using KeyLoad.Core.BlobStorage;
using ManagedCode.Storage.Core.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace KeyLoad.Server;

internal static class FileDownloadExecution
{
    private const string EntityTagQuote = "\"";

    internal static async Task<FileStreamResult> OpenAsync(ControllerBase controller, FileStorageRequest files,
        string path, bool attachment, CancellationToken token)
    {
        var before = await files.ReadAsync(path, Capability.BlobRead, Guid.Empty, token);
        var entry = before.Entry is { Deleted: false } value ? value
            : throw Errors.Fail(ErrorCode.NotFound, FileStorageProtocol.Invalid);
        var physical = await files.Storage.GetStreamAsync(FilePhysicalIdentity.Object(entry.ObjectId), token);
        if (physical.IsFailed)
        {
            throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
        }
        await using var source = physical.Value;
        var snapshot = await VerifiedContentSnapshot.CreateAsync(source, entry.Length, entry.Sha256, token)
            ?? throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
        try
        {
            var current = await files.ReadAsync(path, Capability.BlobRead, Guid.Empty, token);
            if (current.Entry is not { Deleted: false } actual || actual.Incarnation != entry.Incarnation
                || actual.ObjectId != entry.ObjectId || actual.Revision != entry.Revision
                || actual.Sha256 != entry.Sha256 || actual.Length != entry.Length)
            {
                throw Errors.Fail(ErrorCode.Conflict, FileStorageProtocol.Invalid);
            }
            var result = controller.File(snapshot, entry.ContentType, enableRangeProcessing: true);
            result.LastModified = entry.ModifiedOn;
            result.EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(string.Concat(EntityTagQuote, entry.Sha256, EntityTagQuote));
            result.FileDownloadName = attachment ? FileStorageRequest.PublicMetadata(entry).Name : null;
            return result;
        }
        catch (Exception)
        {
            await snapshot.DisposeAsync();
            throw;
        }
    }
}
