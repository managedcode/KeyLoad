using System.Security.Cryptography;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Forwards canonical UTF-8 bytes to a caller-owned incremental hash.</summary>
internal sealed class CanonicalHashStream : Stream
{
    private const int AdjacentElementOffset = 1;
    private const int FirstElementIndex = 0;

    private readonly IncrementalHash hash;
    private bool disposed;

    internal CanonicalHashStream(IncrementalHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        this.hash = hash;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => ThrowIfDisposed();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        hash.AppendData(buffer);
    }

    public override void WriteByte(byte value)
    {
        Span<byte> buffer = stackalloc byte[AdjacentElementOffset];
        buffer[FirstElementIndex] = value;
        Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
