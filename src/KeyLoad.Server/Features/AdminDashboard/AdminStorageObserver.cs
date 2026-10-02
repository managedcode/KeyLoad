using System.Diagnostics;
using System.Security;
using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class AdminStorageObserver
{
    internal const int MaximumEntries = 2_048;
    internal const int MaximumFiles = 200;
    internal const int MaximumMilliseconds = 250;
    private const string Unavailable = "Node storage observation is unavailable.";

    internal static AdminStorageSnapshot Read(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scan = new AdminStorageScan(directory, cancellationToken);
        try
        {
            var root = new DirectoryInfo(directory);
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            { return Missing(); }
            scan.Run(root);
            return scan.Snapshot();
        }
        catch (Exception error) when (FileFailure(error))
        { return Missing(); }
    }

    internal static bool FileFailure(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException;

    private static AdminStorageSnapshot Missing() => new(null, null, null, null, 0, false, [], Unavailable);
}

internal sealed class AdminStorageScan(string rootPath, CancellationToken cancellationToken)
{
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
        directories.Push(root);
        while (directories.Count > 0 && WithinBudget())
        { VisitDirectory(directories.Pop()); }
    }

    private bool WithinBudget()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entries < AdminStorageObserver.MaximumEntries
            && Stopwatch.GetElapsedTime(started).TotalMilliseconds < AdminStorageObserver.MaximumMilliseconds)
        { return true; }
        complete = false;
        return false;
    }

    private void VisitDirectory(DirectoryInfo directory)
    {
        try
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
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
        try
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
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
        cancellationToken.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(rootPath, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
        var category = Category(relative);
        var length = file.Length;
        totalBytes = checked(totalBytes + length);
        observedFiles++;
        switch (category)
        {
            case Canonical: canonicalBytes = checked(canonicalBytes + length); break;
            case Replica: replicaBytes = checked(replicaBytes + length); break;
            case Backup: backupBytes = checked(backupBytes + length); break;
        }
        if (files.Count < AdminStorageObserver.MaximumFiles)
        { files.Add(new(relative, category, length)); }
    }

    private static string Category(string relative)
    {
        var separator = relative.IndexOf('/');
        var parent = separator < 0 ? relative : relative[..separator];
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
