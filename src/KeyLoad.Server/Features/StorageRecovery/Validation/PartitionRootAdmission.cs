using KeyLoad.Replication;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.IO;

namespace KeyLoad.Server;

/// <summary>Admits only the current owned entries at one physical node root.</summary>
internal static class PartitionRootAdmission
{
    private const string InvalidLayout = "The physical node directory is not a supported current layout.";
    private const string SearchIndexDirectory = "search-indexes";
    private const string AnnIndexDirectory = "ann-indexes";
    private const string BackupDirectory = "backups";
    private const FileAttributes NoAttributes = (FileAttributes)0;
    private const FileAttributes DirectoryAttribute = FileAttributes.Directory;
    private const FileAttributes ReparseAttribute = FileAttributes.ReparsePoint;

    /// <summary>Checks an existing root without creating or changing any filesystem entry.</summary>
    /// <returns>True when the root exists and passed current-layout validation.</returns>
    internal static bool InspectBeforeOwnership(string root)
    {
        if (!TryReadRootAttributes(root, out var attributes))
        {
            return false;
        }
        RequireDirectory(attributes);
        InspectEntries(root);
        return true;
    }

    /// <summary>Rechecks the root while its exclusive node-owner handle is retained.</summary>
    internal static void InspectWhileOwned(string root)
    {
        if (!TryReadRootAttributes(root, out var attributes))
        {
            throw Unsupported();
        }
        RequireDirectory(attributes);
        InspectEntries(root);
    }

    private static bool TryReadRootAttributes(string root, out FileAttributes attributes)
    {
        try
        {
            if (new DirectoryInfo(root).LinkTarget is not null)
            {
                throw Unsupported();
            }
            attributes = File.GetAttributes(root);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static void RequireDirectory(FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != DirectoryAttribute)
        {
            throw Unsupported();
        }
    }

    private static void InspectEntries(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            InspectEntry(entry);
        }
    }

    private static void InspectEntry(string path)
    {
        var name = Path.GetFileName(path);
        var attributes = File.GetAttributes(path);
        if ((attributes & ReparseAttribute) != NoAttributes)
        {
            throw Unsupported();
        }
        if (name == PartitionStoreProtocol.OwnershipFile)
        {
            if ((attributes & DirectoryAttribute) != NoAttributes)
            {
                throw Unsupported();
            }
            try
            {
                OfflineRegularFile.RequireRegular(path);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.FormatUnsupported)
            {
                throw Unsupported();
            }
            return;
        }
        if (!IsCurrentDirectory(name) || (attributes & DirectoryAttribute) == NoAttributes)
        {
            throw Unsupported();
        }
    }

    private static bool IsCurrentDirectory(string name) => name is PartitionStoreProtocol.CanonicalDirectory
        or ReplicaProtocol.ReplicaDirectory or ReplicaProtocol.SnapshotDirectory
        or SearchIndexDirectory or AnnIndexDirectory or NativeTextIncrementalProtocol.RootDirectory or BackupDirectory;

    private static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.FormatUnsupported, InvalidLayout);
}
