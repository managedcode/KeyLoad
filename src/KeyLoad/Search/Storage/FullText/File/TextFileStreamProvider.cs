using Microsoft.Extensions.Options;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Search;

internal sealed partial class TextFileStreamProvider(string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership resources, bool allowReadOnlyInventory = false)
    : IFileStreamProvider
{
    private const int RuntimeDefaultStreamBufferBytes = 4_096;
    private readonly LocalFileStreamProvider inner = new();
    private readonly int fileBufferBytes = ReadFileBufferBytes(executionOptions);
    private readonly TextPathAccess pathAccess = new(root, leaf, sourceNodeId, executionOptions: executionOptions, resources: resources);

    public IFileStream CreateFileStream(string path, FileMode mode, FileAccess access, FileShare share,
        int bufferSize = RuntimeDefaultStreamBufferBytes, FileOptions options = FileOptions.None)
    {
        var full = pathAccess.Resolve(path);
        if (allowReadOnlyInventory && share == FileShare.None)
        {
            share = FileShare.Read;
        }
        return resources.MutatePhysical(() => OpenOwned(full, mode, access, share, bufferSize, options));
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

    public void CreateDirectory(string path) => pathAccess.CreateDirectory(path, inner);

    public void DeleteFile(string path)
        => resources.MutatePhysical(() => DeleteOwned(path));

    private void DeleteOwned(string path)
    {
        var full = pathAccess.Resolve(path);
        _ = TextFiles.ReadOwnerForProvider(root, leaf, sourceNodeId, executionOptions);
        try
        {
            _ = File.GetAttributes(full);
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        pathAccess.Require(full, directory: false);
        resources.DeleteOwnedFile(full, () => inner.DeleteFile(full));
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: true);
        if (recursive)
        {
            TextFiles.RuntimeValidateTrackedLayout(Path.Combine(root, leaf),
                TextFiles.ReadOwnerForProvider(root, leaf, sourceNodeId, executionOptions: executionOptions), executionOptions: executionOptions);
        }
        resources.DeleteOwnedDirectory(full, () => inner.DeleteDirectory(full, recursive));
    }

    public string ReadAllText(string path)
        => inner.ReadAllText(RequireReadableFile(path));

    public byte[] ReadAllBytes(string path)
        => inner.ReadAllBytes(RequireReadableFile(path));

    private string RequireReadableFile(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: false);
        TextFileIO.VerifyBoundedFile(full, executionOptions: executionOptions);
        return full;
    }

    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
        => resources.MutatePhysical(() => ReplaceOwned(sourceFileName, destinationFileName, destinationBackupFileName));

    private void ReplaceOwned(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
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
        pathAccess.PrepareReplaceTargets(destination, backup);
        resources.ReplaceOwnedFiles(source, destination, backup, () => inner.Replace(source, destination, backup));
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
                throw TextErrors.BoundExceeded();
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

    private static int ReadFileBufferBytes(IOptions<TextExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
        var snapshot = executionOptions.Value;
        snapshot.Validate();
        return snapshot.FileBufferBytes;
    }
}
