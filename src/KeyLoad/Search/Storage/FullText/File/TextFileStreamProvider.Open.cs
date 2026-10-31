using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Search;

internal sealed partial class TextFileStreamProvider
{
    private const string OccupiedMoveDestination = "The owned move destination already exists.";

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
