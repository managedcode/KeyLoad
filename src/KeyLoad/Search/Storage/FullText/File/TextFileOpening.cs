using KeyLoad.Clustering;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Search;

internal static class TextFileOpening
{
    private const long UncreatedFileBytes = 0;

    internal static IFileStream Open(LocalFileStreamProvider inner, TextPathAccess pathAccess,
        TextResourceOwnership? resources, int fileBufferBytes, string full, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        resources?.RequireKnownFile(full);
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
        var reservation = resources?.ReserveFile(full, UncreatedFileBytes,
            () => File.Exists(full) ? new FileInfo(full).Length : UncreatedFileBytes);
        IFileStream? stream = null;
        var failures = new List<Exception>();
        try
        {
            stream = inner.CreateFileStream(full, mode, access, share, Math.Min(bufferSize, fileBufferBytes), options);
            if (resources is not null)
            {
                stream = new TextBoundedFileStream(stream, resources);
            }
            if (created)
            {
                TextFileIO.SetPrivateFileMode(full);
            }
            reservation?.CompleteAfterJoinedWrite();
            return stream;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        var beforeClose = failures.Count;
        if (stream is not null)
        {
            ServerFailureObserver.Observe(stream.Dispose, failures);
        }
        if (failures.Count == beforeClose && reservation is not null)
        {
            ServerFailureObserver.Observe(reservation.CompleteAfterJoinedWrite, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        throw TextErrors.Ownership();
    }

    private static bool CreatesOrOpens(FileMode mode)
        => mode is FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate or FileMode.Append;
}
