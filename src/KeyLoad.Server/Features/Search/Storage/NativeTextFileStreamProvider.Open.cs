using KeyLoad.Orleans;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextFileStreamProvider
{
    private const long UncreatedFileBytes = 0;

    private IFileStream OpenOwned(string full, FileMode mode, FileAccess access, FileShare share,
        int bufferSize, FileOptions options)
    {
        resources?.RequireKnownFile(full);
        var exists = File.Exists(full);
        var created = false;
        if (exists)
        { pathAccess.Require(full, directory: false); }
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
            { stream = new NativeTextBoundedFileStream(stream, resources); }
            if (created)
            { NativeTextFileIO.SetPrivateFileMode(full); }
            reservation?.CompleteAfterJoinedWrite();
            return stream;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        var beforeClose = failures.Count;
        if (stream is not null)
        { ServerFailureObserver.Observe(stream.Dispose, failures); }
        if (failures.Count == beforeClose && reservation is not null)
        { ServerFailureObserver.Observe(reservation.CompleteAfterJoinedWrite, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        throw NativeTextErrors.Ownership();
    }

}
