using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.BlobStorage;
using ManagedCode.Storage.Core.Models;

namespace KeyLoad.Server;

internal static class FileUploadPublication
{

    internal static async Task<BlobMetadata> ExecuteAsync(FileStorageRequest files, FileUpload reserved,
        Func<FileUpload, CancellationToken, Task<BlobMetadata>> upload, CancellationToken token,
        ImmutableDictionary<string, string>? metadata = null)
    {
        var staged = await files.StageAsync(reserved, token, metadata);
        string hash;
        try
        {
            var result = await upload(staged, token);
            if (result.Length != checked((ulong)staged.Length))
            {
                throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
            }
            var content = await files.Storage.GetStreamAsync(FilePhysicalIdentity.Object(staged.ObjectId), token);
            content.ThrowIfFail();
            await using var source = content.Value!;
            if (!source.CanSeek || source.Length != staged.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, FileStorageProtocol.Invalid);
            }
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, token));
        }
        catch (Exception)
        {
            await AbortJoinedAsync(files, staged);
            throw;
        }
        // A publication may commit before its reply is lost. Never clean up from this boundary.
        var entry = await files.PublishAsync(staged, hash, metadata ?? ImmutableDictionary<string, string>.Empty, token);
        return FileStorageRequest.PublicMetadata(entry);
    }

    private static async Task AbortJoinedAsync(FileStorageRequest files, FileUpload staged)
    {
        using var deadline = new CancellationTokenSource(files.CleanupTimeout);
        try
        {
            var aborted = await files.AbortAsync(staged, staged.ObjectId, deadline.Token);
            var deleted = await files.Storage.DeleteAsync(FilePhysicalIdentity.Object(staged.ObjectId), deadline.Token);
            if (deleted.IsSuccess)
            {
                _ = await files.ReclaimAsync(aborted, deadline.Token);
            }
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            // Persisted reservations retain the physical identity when authorized cleanup cannot settle.
        }
    }
}
