using System.Security.Cryptography;

namespace KeyLoad.Server;

/// <summary>Owns one fixed native buffer for sequential canonical JSON writes.</summary>
internal sealed class McpBoundedWriteStream : Stream
{
    private const int SingleByteWidth = 1;
    private const string UnsupportedOperation = "The canonical JSON stream supports sequential writes only.";
    private readonly MemoryStream buffer;
    private readonly int maximumBytes;
    private bool disposed;

    /// <summary>Allocates the single private write capacity without permitting later growth.</summary>
    /// <param name="maximumBytes">The positive inclusive byte ceiling.</param>
    internal McpBoundedWriteStream(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        this.maximumBytes = maximumBytes;
        buffer = new MemoryStream(maximumBytes);
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !disposed;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException(UnsupportedOperation);

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException(UnsupportedOperation);
        set => throw new NotSupportedException(UnsupportedOperation);
    }

    /// <inheritdoc />
    public override void Flush() => ThrowIfDisposed();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        { return Task.FromCanceled(cancellationToken); }
        Flush();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(UnsupportedOperation);

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => throw new NotSupportedException(UnsupportedOperation);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<int>(cancellationToken)
            : ValueTask.FromException<int>(new NotSupportedException(UnsupportedOperation));

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(UnsupportedOperation);

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException(UnsupportedOperation);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ValidateWrite(buffer.Length);
        this.buffer.Write(buffer);
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        ValidateWrite(SingleByteWidth);
        buffer.WriteByte(value);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        { return ValueTask.FromCanceled(cancellationToken); }
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <summary>Gets a read-only borrowed view of the entire private capacity while this owner is alive.</summary>
    /// <returns>A view that remains owned here and observes clearing on disposal.</returns>
    internal ReadOnlyMemory<byte> BorrowBuffer()
    {
        const int StartEmptyCount = 0;

        ThrowIfDisposed();
        return buffer.GetBuffer().AsMemory(StartEmptyCount, maximumBytes);
    }

    /// <summary>Gets the actual written range without allocating a returned copy of the private buffer.</summary>
    internal int WrittenBytes
    {
        get { ThrowIfDisposed(); return checked((int)buffer.Length); }
    }

    /// <summary>Copies only the accepted bytes into an independent owner.</summary>
    /// <returns>An exact-length copy which survives disposal of the private capacity.</returns>
    internal byte[] ToOwnedArray()
    {
        ThrowIfDisposed();
        return buffer.ToArray();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
            buffer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ValidateWrite(int count)
    {
        ThrowIfDisposed();
        if (count > maximumBytes - buffer.Length)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.CanonicalPayloadExceeded); }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
