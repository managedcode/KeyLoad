using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Search;

internal sealed partial class TextFileStreamProvider
{
    private const string OccupiedMoveDestination = "The owned move destination already exists.";

    public void SyncDirectory(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: true);
        inner.SyncDirectory(full);
    }

    public IReadOnlyList<string> GetFiles(string path)
    {
        var full = pathAccess.Resolve(path);
        pathAccess.Require(full, directory: true);
        var files = new List<string>();
        foreach (var child in inner.GetFiles(full))
        {
            if (files.Count == executionOptions.Value.MaximumFiles)
            {
                throw TextErrors.BoundExceeded();
            }
            var resolved = pathAccess.Resolve(child);
            pathAccess.Require(resolved, directory: false);
            files.Add(resolved);
        }
        return files;
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        var source = pathAccess.Resolve(sourcePath);
        var destination = pathAccess.Resolve(destinationPath);
        pathAccess.Require(source, directory: false);
        if (File.Exists(destination))
        {
            pathAccess.Require(destination, directory: false);
            throw new IOException(OccupiedMoveDestination);
        }
        pathAccess.PrepareReplaceTarget(destination);
        if (resources is null)
        {
            inner.MoveFile(source, destination);
        }
        else
        {
            resources.ReplaceOwnedFiles(source, destination, null, () => inner.MoveFile(source, destination));
        }
    }

    private IFileStream OpenOwned(string full, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        => TextFileOpening.Open(inner, pathAccess, resources, fileBufferBytes, full, mode, access, share, bufferSize, options);

}
