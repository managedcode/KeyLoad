using KeyLoad.Storage.IO;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeSnapshotUpgradeFileLease : IDisposable
{
    private readonly FileStream stream;
    private Exception? primaryFailure;

    private ZoneTreeSnapshotUpgradeFileLease(FileStream stream) => this.stream = stream;

    internal FileStream Stream => stream;

    internal static ZoneTreeSnapshotUpgradeFileLease OpenSource(string path)
    {
        FileStream? stream = null;
        try
        {
            stream = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.None,
                ZoneTreePersistenceFormat.FileBufferBytes);
            var lease = new ZoneTreeSnapshotUpgradeFileLease(stream);
            stream = null;
            return lease;
        }
        finally
        { stream?.Dispose(); }
    }

    internal static ZoneTreeSnapshotUpgradeFileLease CreatePrivateOutput(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = ZoneTreePersistenceFormat.FileBufferBytes,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, options);
            var lease = new ZoneTreeSnapshotUpgradeFileLease(stream);
            stream = null;
            return lease;
        }
        finally
        { stream?.Dispose(); }
    }

    internal void RecordPrimary(Exception failure) => primaryFailure ??= failure;

    public void Dispose()
    {
        try
        { stream.Dispose(); }
        catch (Exception cleanup) when (primaryFailure is not null)
        { throw new AggregateException(primaryFailure, cleanup); }
    }
}
