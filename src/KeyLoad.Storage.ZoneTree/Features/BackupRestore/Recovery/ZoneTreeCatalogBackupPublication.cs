namespace KeyLoad.Storage.ZoneTree;

/// <summary>Publishes only a complete original native archive from its owning unpublished staging root.</summary>
internal static class ZoneTreeCatalogBackupPublication
{
    internal const int EmptyMetadata = 0;
    internal const string MissingMetadata = "The native catalog capture returned no metadata.";
    internal const string MetadataExceeded = "The native catalog capture exceeds the configured manifest byte bound.";
    private const string InvalidTarget = "The native catalog backup destination is not clean.";
    private const string StagingPrefix = ".keyload-catalog-backup-";
    private const string GuidFormat = "N";
    private const int NoFileAttributes = 0;

    internal static NativeCatalogBackupCapture Create(ZoneTreeStoreRuntime runtime, string directory, ReadOnlyMemory<byte> metadata,
        Action<NativeCatalogBackupCapture>? requireCaptured, CancellationToken cancellationToken)
    {
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        RequireClean(target);
        var existed = Directory.Exists(target);
        var originalMode = existed && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(target) : (UnixFileMode?)null;
        var parent = Path.GetDirectoryName(target) ?? throw Errors.Fail(ErrorCode.Validation, InvalidTarget);
        var staging = Path.Combine(parent, StagingPrefix + Guid.NewGuid().ToString(GuidFormat));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = ZoneTreeBackupRestoreFiles.Create(runtime, staging);
            var digest = ZoneTreeCatalogBackupMetadataFile.Write(runtime, staging, position, metadata);
            var result = new NativeCatalogBackupCapture(position, metadata, digest);
            requireCaptured?.Invoke(result);
            cancellationToken.ThrowIfCancellationRequested();
            RequireClean(target);
            if (Directory.Exists(target))
            { Directory.Delete(target, recursive: false); }
            Directory.Move(staging, target);
            return result;
        }
        catch (Exception primary)
        {
            RestoreUnpublished(staging, target, existed, originalMode, primary);
            throw;
        }
    }

    private static void RestoreUnpublished(string staging, string target, bool existed,
        UnixFileMode? originalMode, Exception primary)
    {
        try
        {
            if (Directory.Exists(staging))
            { Directory.Delete(staging, recursive: true); }
            if (existed && !Directory.Exists(target))
            { RestoreOriginalEmpty(target, originalMode); }
        }
        catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
    }

    private static void RestoreOriginalEmpty(string target, UnixFileMode? originalMode)
    {
        Directory.CreateDirectory(target);
        if (!OperatingSystem.IsWindows() && originalMode is { } mode)
        { File.SetUnixFileMode(target, mode); }
    }

    private static void RequireClean(string target)
    {
        RequirePath(target);
        if (File.Exists(target) || Directory.Exists(target)
            && ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != NoFileAttributes
                || Directory.EnumerateFileSystemEntries(target).Any()))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidTarget); }
    }

    internal static void RequirePath(string target)
    {
        var ancestor = new DirectoryInfo(Path.GetFullPath(target));
        while (ancestor is not null)
        {
            if (File.Exists(ancestor.FullName) || ancestor.Exists
                && (ancestor.Attributes & FileAttributes.ReparsePoint) != NoFileAttributes)
            { throw Errors.Fail(ErrorCode.Conflict, InvalidTarget); }
            ancestor = ancestor.Parent;
        }

    }
}
