using ManagedCode.Communication;
using ManagedCode.Storage.Core;
using ManagedCode.Storage.Core.Models;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;

namespace KeyLoad.Artifacts;

public static class ArtifactTransfer
{
    public static async Task<Result<BlobMetadata>> CopyToFileStorageAsync(string artifactPath, string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Path.GetFullPath(artifactPath));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw Errors.Fail(ErrorCode.Validation, "The archive source must be a regular file.");
        if (!Directory.Exists(destinationRoot) && !OperatingSystem.IsWindows())
            Directory.CreateDirectory(destinationRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using IStorage storage = new FileSystemStorage(new FileSystemStorageOptions { BaseFolder = Path.GetFullPath(destinationRoot) });
        return await storage.UploadAsync(file, new UploadOptions(file.Name, mimeType: "application/octet-stream"), cancellationToken).ConfigureAwait(false);
    }
}
