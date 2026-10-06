using System.Diagnostics;
using System.Security;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class AdminStorageObserver
{
    private const int MissingObservedFilesEmptyCount = 0;

    private const string Unavailable = "Node storage observation is unavailable.";

    internal static AdminStorageSnapshot Read(string directory, CancellationToken cancellationToken,
        IOptions<AdminObservationOptions> options)
    {
        const int EmptyRootAttributesFileAttributesReparsePoint = 0;

        cancellationToken.ThrowIfCancellationRequested();
        options.Value.Validate();
        var scan = new AdminStorageScan(directory, cancellationToken, options);
        try
        {
            var root = new DirectoryInfo(directory);
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != EmptyRootAttributesFileAttributesReparsePoint)
            { return Missing(); }
            scan.Run(root);
            return scan.Snapshot();
        }
        catch (Exception error) when (FileFailure(error))
        { return Missing(); }
    }

    internal static bool FileFailure(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException;

    private static AdminStorageSnapshot Missing() => new(null, null, null, null, MissingObservedFilesEmptyCount, false, [], Unavailable);
}

internal sealed class AdminStorageScan(string rootPath, CancellationToken cancellationToken,
    IOptions<AdminObservationOptions> options)
{
    private readonly AdminObservationOptions settings = options.Value;
    private const string Canonical = "canonical";
    private const string Replica = "replica";
    private const string Backup = "backup";
    private const string Other = "other";
    private const string BackupDirectory = "backups";
    private const string Incomplete = "Storage observation is incomplete because files changed, were unavailable, or exceeded observation bounds.";
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly List<AdminFileInfo> files = [];
    private readonly Stack<DirectoryInfo> directories = [];
    private long canonicalBytes;
    private long replicaBytes;
    private long backupBytes;
    private long totalBytes;
    private int observedFiles;
    private int entries;
    private bool complete = true;

    internal void Run(DirectoryInfo root)
    {
        const int DirectoriesCountValidationBoundary = 0;

        directories.Push(root);
        while (directories.Count > DirectoriesCountValidationBoundary && WithinBudget())
        { VisitDirectory(directories.Pop()); }
        _ = WithinBudget();
    }

    private bool WithinBudget()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entries < settings.MaximumEntries
            && Stopwatch.GetElapsedTime(started) < settings.ScanDeadline)
        { return true; }
        complete = false;
        return false;
    }

    private void VisitDirectory(DirectoryInfo directory)
    {
        const int EmptyDirectoryAttributesFileAttributesReparsePoint = 0;

        try
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != EmptyDirectoryAttributesFileAttributesReparsePoint)
            { complete = false; return; }
            using var iterator = directory.EnumerateFileSystemInfos().GetEnumerator();
            while (WithinBudget() && iterator.MoveNext())
            {
                entries++;
                VisitEntry(iterator.Current);
            }
        }
        catch (Exception error) when (AdminStorageObserver.FileFailure(error))
        { complete = false; }
    }

    private void VisitEntry(FileSystemInfo entry)
    {
        const int EmptyEntryAttributesFileAttributesReparsePoint = 0;

        try
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != EmptyEntryAttributesFileAttributesReparsePoint)
            { complete = false; return; }
            if (entry is DirectoryInfo directory)
            { directories.Push(directory); return; }
            if (entry is FileInfo file)
            { ObserveFile(file); }
        }
        catch (Exception error) when (AdminStorageObserver.FileFailure(error))
        { complete = false; }
    }

    private void ObserveFile(FileInfo file)
    {
        const char SlashCharacter = '/';

        cancellationToken.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(rootPath, file.FullName).Replace(Path.DirectorySeparatorChar, SlashCharacter);
        var category = Category(relative);
        var length = file.Length;
        totalBytes = checked(totalBytes + length);
        observedFiles++;
        switch (category)
        {
            case Canonical:
                canonicalBytes = checked(canonicalBytes + length);
                break;
            case Replica:
                replicaBytes = checked(replicaBytes + length);
                break;
            case Backup:
                backupBytes = checked(backupBytes + length);
                break;
        }
        if (files.Count < settings.MaximumRetainedFiles)
        { files.Add(new(relative, category, length)); }
    }

    private static string Category(string relative)
    {
        const char SlashCharacter = '/';
        const int SeparatorValidationBoundary = 0;

        var separator = relative.IndexOf(SlashCharacter, StringComparison.Ordinal);
        var parent = separator < SeparatorValidationBoundary ? relative : relative[..separator];
        return parent switch
        {
            PartitionStoreProtocol.CanonicalDirectory => Canonical,
            ReplicaProtocol.ReplicaDirectory => Replica,
            BackupDirectory => Backup,
            _ => Other
        };
    }

    internal AdminStorageSnapshot Snapshot() => new(canonicalBytes, replicaBytes, backupBytes, totalBytes,
        observedFiles, complete, [.. files], complete ? null : Incomplete);
}
