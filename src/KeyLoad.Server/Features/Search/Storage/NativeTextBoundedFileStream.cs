using KeyLoad.Orleans;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextBoundedFileStream : Stream, IFileStream
{
    private readonly IFileStream original;
    private readonly NativeTextBoundedStreamWrites writes;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private readonly NativeTextResourceOwnership resources;
    private readonly NativeTextOpenFileGroup group;

    internal NativeTextBoundedFileStream(IFileStream original, NativeTextResourceOwnership resources)
    {
        this.original = original;
        this.resources = resources;
        group = resources.OpenFile(original.FilePath, original);
        writes = new(original, resources, group);
    }

    public string FilePath => original.FilePath;
    public override bool CanRead => original.CanRead;
    public override bool CanWrite => original.CanWrite;
    public override bool CanSeek => original.CanSeek;
    public override bool CanTimeout => original.CanTimeout;
    public override long Length => original.Length;
    public override long Position
    {
        get => original.Position;
        set => writes.MutatePosition(() => { original.Position = value; return value; });
    }
    public override int ReadTimeout { get => original.ReadTimeout; set => original.ReadTimeout = value; }
    public override int WriteTimeout { get => original.WriteTimeout; set => original.WriteTimeout = value; }
    public Stream ToStream() => this;
    public int ReadFaster(byte[] buffer, int offset, int count) => original.ReadFaster(buffer, offset, count);
    public override int Read(byte[] buffer, int offset, int count) => original.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => original.Read(buffer);
    public override int ReadByte() => original.ReadByte();
    public override long Seek(long offset, SeekOrigin origin)
        => writes.MutatePosition(() => original.Seek(offset, origin));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => original.ReadAsync(buffer, offset, count, token);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        => original.ReadAsync(buffer, token);
    public override void Flush() => original.Flush();
    public void Flush(bool flushToDisk) => original.Flush(flushToDisk);
    public override Task FlushAsync(CancellationToken token) => original.FlushAsync(token);

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var reservation = writes.BeginWrite(buffer.Length);
        var failures = new List<Exception>();
        try
        { original.Write(buffer); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        writes.Complete(reservation, failures);
    }

    public override void WriteByte(byte value)
    {
        var reservation = writes.BeginWrite(sizeof(byte));
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => original.WriteByte(value), failures);
        writes.Complete(reservation, failures);
    }

    public override void SetLength(long value)
    {
        var reservation = writes.Begin(() => value);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => original.SetLength(value), failures);
        writes.Complete(reservation, failures);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => writes.WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        => writes.WriteAsync(buffer, token);

    protected override void Dispose(bool disposing)
    {
        var failures = new List<Exception>();
        if (disposing)
        { ServerFailureObserver.Observe(() => SharedClose().GetAwaiter().GetResult(), failures); }
        ServerFailureObserver.Observe(() => base.Dispose(disposing), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public override async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(SharedClose, failures).ConfigureAwait(false);
        try
        { await base.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { if (!failures.Contains(error)) { failures.Add(error); } }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { if (!failures.Contains(error)) { failures.Add(error); } }
        GC.SuppressFinalize(this);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private Task SharedClose()
    {
        lock (disposalGate)
        { return disposal ??= CloseCoreAsync(); }
    }

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(writes.JoinForDisposalAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => resources.BeginCloseFile(group, original), failures);
        var beforeNativeClose = failures.Count;
        try
        { await original.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        if (failures.Count == beforeNativeClose)
        { ServerFailureObserver.Observe(() => resources.CloseFile(group, original), failures); }
        ServerFailureObserver.Observe(writes.ThrowRetained, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
