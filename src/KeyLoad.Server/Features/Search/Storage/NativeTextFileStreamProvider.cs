using Microsoft.Extensions.Options;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextFileStreamProvider(string root, string leaf, Guid sourceNodeId, IOptions<NativeTextExecutionOptions> executionOptions)
    : IFileStreamProvider
{
    private const int NativeDefaultStreamBufferBytes = 4_096;
    private readonly LocalFileStreamProvider inner = new();
    private readonly int fileBufferBytes = ReadFileBufferBytes(executionOptions);
    private readonly NativeTextPathAccess pathAccess = new(root, leaf, sourceNodeId, executionOptions: executionOptions);

    public IFileStream CreateFileStream(string path, FileMode mode, FileAccess access, FileShare share,
        int bufferSize = NativeDefaultStreamBufferBytes, FileOptions options = FileOptions.None)
    {
        var full = pathAccess.Resolve(path);
        var exists = File.Exists(full);
        var created = false;
        if (exists)
        {
            pathAccess.Require(full, directory: false);
        }
        else if (CreatesOrOpens(mode))
        {
            pathAccess.Track(full, directory: false);
            mode = FileMode.CreateNew;
            created = true;
        }
        var stream = inner.CreateFileStream(full, mode, access, share, Math.Min(bufferSize, fileBufferBytes), options);
        if (created)
        {
            NativeTextFileIO.SetPrivateFileMode(full);
        }
        return stream;
    }

    public bool FileExists(string path)
    {
        var full = pathAccess.Resolve(path);
        if (File.Exists(full))
        {
            pathAccess.Require(full, directory: false);
        }
        return inner.FileExists(full);
    }

    public bool DirectoryExists(string path)
    {
        var full = pathAccess.Resolve(path);
        if (Directory.Exists(full))
        {
            pathAccess.Require(full, directory: true);
        }
        return inner.DirectoryExists(full);
    }

    public void CreateDirectory(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.TrackDirectoryChain(full);
        inner.CreateDirectory(full);
        pathAccess.SetPrivateDirectoryModes(full);
    }

    public void DeleteFile(string path)
    {
        var full = pathAccess.Resolve(path);
        NativeTextFiles.RequireNativePath(root, leaf, sourceNodeId, full, directory: false, executionOptions: executionOptions);
        if (File.Exists(full))
        {
            pathAccess.Require(full, directory: false);
        }
        inner.DeleteFile(full);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: true);
        if (recursive)
        {
            NativeTextFiles.ValidateTrackedNativeLayout(Path.Combine(root, leaf),
                NativeTextFiles.ReadOwnerForProvider(root, leaf, sourceNodeId, executionOptions: executionOptions), executionOptions: executionOptions);
        }
        inner.DeleteDirectory(full, recursive);
    }

    public string ReadAllText(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: false);
        NativeTextFileIO.VerifyBoundedFile(full, executionOptions: executionOptions);
        return inner.ReadAllText(full);
    }

    public byte[] ReadAllBytes(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: false);
        NativeTextFileIO.VerifyBoundedFile(full, executionOptions: executionOptions);
        return inner.ReadAllBytes(full);
    }

    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        var source = pathAccess.Resolve(sourceFileName);
        var destination = pathAccess.Resolve(destinationFileName);
        var backup = destinationBackupFileName is null
            ? null
            : pathAccess.Resolve(destinationBackupFileName);
        pathAccess.Require(source, directory: false);
        if (backup is not null && File.Exists(backup))
        {
            pathAccess.Require(backup, directory: false);
        }
        pathAccess.PrepareReplaceTarget(destination);
        if (backup is not null)
        {
            pathAccess.PrepareReplaceTarget(backup);
        }
        inner.Replace(source, destination, backup);
    }

    public DurableFileWriter GetDurableFileWriter() => new(this);

    public IReadOnlyList<string> GetDirectories(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: true);
        var directories = new List<string>(executionOptions.Value.MaximumDirectories);
        foreach (var child in Directory.EnumerateDirectories(full))
        {
            if (directories.Count == executionOptions.Value.MaximumDirectories)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            var resolved = pathAccess.Resolve(child);
            pathAccess.Require(resolved, directory: true);
            directories.Add(resolved);
        }
        return directories;
    }

    public string CombinePaths(string path1, string path2)
    {
        var combined = inner.CombinePaths(path1, path2);
        return pathAccess.Resolve(combined);
    }

    private static bool CreatesOrOpens(FileMode mode)
        => mode is FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate or FileMode.Append;

    private static int ReadFileBufferBytes(IOptions<NativeTextExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        var snapshot = executionOptions.Value;
        snapshot.Validate();
        return snapshot.FileBufferBytes;
    }
}
