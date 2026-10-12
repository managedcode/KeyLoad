using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireFiles(string root, IOptions<RequestProbeExecutionOptions> limits)
{
    private const long EmptyRecordBytes = 0;
    private const long EmptyLockBytes = 0;
    private const int EndOfStream = -1;
    private readonly NativeBlobWireFileInventory inventory = new(root, limits);

    internal bool Exists(string name)
    { _ = inventory.Read(); return File.Exists(PathFor(name)); }

    internal byte[] Read(string name)
    {
        _ = inventory.Read();
        var path = PathFor(name);
        var length = NativeBlobWireFileInventory.RequireFile(path);
        if (length <= EmptyRecordBytes || length > limits.Value.MaximumRecordBytes)
        { throw Invalid(); }
        var failures = new List<Exception>();
        byte[] bytes = [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ServerFailureObserver.Observe(() =>
            {
                bytes = new byte[checked((int)length)];
                stream.ReadExactly(bytes);
                if (stream.ReadByte() != EndOfStream || NativeBlobWireFileInventory.RequireFile(path) != length)
                { throw Invalid(); }
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return bytes;
    }

    internal FileStream Publication()
        => OpenLock(NativeBlobWireProbeProtocol.PublicationFile);

    internal T WithPublication<T>(Func<T> action)
    {
        var failures = new List<Exception>();
        T result = default!;
        try
        {
            using var stream = Publication();
            ServerFailureObserver.Observe(() => { result = action(); }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    internal void WithPublication(Action action)
        => WithPublication(() => { action(); return true; });

    internal void WriteLocked(string name, byte[] bytes)
    {
        var before = inventory.Read();
        var destination = PathFor(name);
        if (bytes.Length > limits.Value.MaximumRecordBytes || File.Exists(destination)
            || before.Count >= limits.Value.MaximumFiles
            || bytes.Length > limits.Value.MaximumAggregateBytes - before.Bytes)
        { throw Invalid(); }
        var temporary = Path.Combine(root, RequestCqrsProbeProtocol.TemporaryFilePrefix
            + Guid.NewGuid().ToString(RequestCqrsProbeProtocol.SessionIdFormat) + NativeBlobWireNames.TemporarySuffix);
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = limits.Value.FileBufferBytes,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        { streamOptions.UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode; }
        var failures = new List<Exception>();
        try
        {
            using var stream = new FileStream(temporary, streamOptions);
            ServerFailureObserver.Observe(() => { stream.Write(bytes); stream.Flush(flushToDisk: true); }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        _ = inventory.Read();
        File.Move(temporary, destination, overwrite: false);
    }

    internal void RemoveLocked(string name, byte[] expected)
    {
        if (!Read(name).AsSpan().SequenceEqual(expected))
        { throw Invalid(); }
        File.Delete(PathFor(name));
        _ = inventory.Read();
    }

    private FileStream OpenLock(string name)
    {
        _ = inventory.Read();
        var path = PathFor(name);
        if (NativeBlobWireFileInventory.RequireFile(path) != EmptyLockBytes)
        { throw Invalid(); }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = limits.Value.FileBufferBytes
        });
    }

    private string PathFor(string name)
    {
        if (name != Path.GetFileName(name))
        { throw Invalid(); }
        return Path.Combine(root, name);
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
