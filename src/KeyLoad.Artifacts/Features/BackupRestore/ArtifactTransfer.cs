using ManagedCode.Communication;
using ManagedCode.Storage.Core.Models;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;
using StorageBlobMetadata = ManagedCode.Storage.Core.Models.BlobMetadata;

namespace KeyLoad.Artifacts;

/// <summary>Copies a backup archive to the configured file-storage destination.</summary>
public static class ArtifactTransfer
{
    private const string InvalidArchiveSource = "The archive source must be a regular file.";
    private const string ArchiveMimeType = "application/octet-stream";

    /// <summary>Transfers a regular local archive to file storage without retaining its bytes.</summary>
    /// <param name="artifactPath">Existing archive file path.</param>
    /// <param name="destinationRoot">File-storage destination root.</param>
    /// <param name="cancellationToken">Cancellation propagated to the upload.</param>
    /// <returns>The file-storage upload result and metadata.</returns>
    public static async Task<Result<StorageBlobMetadata>> CopyToFileStorageAsync(string artifactPath, string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Path.GetFullPath(artifactPath));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidArchiveSource);
        }
        if (!Directory.Exists(destinationRoot) && !OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(destinationRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        using var storage = new FileSystemStorage(new FileSystemStorageOptions { BaseFolder = Path.GetFullPath(destinationRoot) });
        return await storage.UploadAsync(file, new UploadOptions(file.Name, mimeType: ArchiveMimeType), cancellationToken).ConfigureAwait(false);
    }
}
