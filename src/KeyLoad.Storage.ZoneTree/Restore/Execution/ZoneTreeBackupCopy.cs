using ManagedCode.Storage.Core.Models;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Copies a backup directory into ManagedCode.Storage file storage, keeping its relative layout.</summary>
public static class ZoneTreeBackupCopy
{
    private const string AllFiles = "*";
    private const string SameDirectory = ".";
    private const string BackupMimeType = "application/octet-stream";
    private const string CopyFailed = "A backup file could not be copied to file storage.";
    private const string Overlap = "Backup source and destination directories must not overlap.";
    private const string NotEmpty = "The backup copy destination must be empty.";

    /// <summary>Uploads every file of the backup directory to the file-storage root.</summary>
    /// <param name="backupDirectory">Directory written by a backup.</param>
    /// <param name="destinationRoot">File-storage destination root.</param>
    /// <param name="cancellationToken">Cancellation propagated to each upload.</param>
    /// <returns>Completion once every file is uploaded.</returns>
    public static async Task CopyToFileStorageAsync(string backupDirectory, string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupDirectory));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(backupDirectory);
        }
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var sourcePrefix = Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar;
        var destinationPrefix = Path.EndsInDirectorySeparator(destination) ? destination : destination + Path.DirectorySeparatorChar;
        if (source.Equals(destination, comparison) || source.StartsWith(destinationPrefix, comparison)
            || destination.StartsWith(sourcePrefix, comparison))
        {
            throw Errors.Fail(ErrorCode.Validation, Overlap);
        }
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, NotEmpty);
        }
        ZoneTreeStoreFiles.CreatePrivateDirectory(destination);
        using var storage = new FileSystemStorage(new FileSystemStorageOptions { BaseFolder = destination });
        foreach (var path in Directory.EnumerateFiles(source, AllFiles, SearchOption.AllDirectories))
        {
            var file = new FileInfo(path);
            var directory = Path.GetRelativePath(source, file.DirectoryName!);
            var uploaded = await storage.UploadAsync(file, new UploadOptions(file.Name,
                directory == SameDirectory ? null : directory, BackupMimeType), cancellationToken);
            if (uploaded.IsFailed)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, CopyFailed);
            }
        }
    }
}
