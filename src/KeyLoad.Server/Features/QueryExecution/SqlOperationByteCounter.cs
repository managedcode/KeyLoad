namespace KeyLoad.Server;

/// <summary>Measures the complete envelope with cancellation and no retained serialized copy.</summary>
internal sealed class SqlOperationByteCounter(int maximumBytes, CancellationToken cancellationToken) : Stream
{
    private long written;

    /// <inheritdoc />
    public override bool CanRead => false;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => true;
    /// <inheritdoc />
    public override long Length => written;
    /// <inheritdoc />
    public override long Position
    {
        get => written;
        set => throw new NotSupportedException(SqlOperationSyntax.SequentialOnly);
    }

    /// <inheritdoc />
    public override void Flush() => cancellationToken.ThrowIfCancellationRequested();
    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException(SqlOperationSyntax.SequentialOnly);
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException(SqlOperationSyntax.SequentialOnly);
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException(SqlOperationSyntax.SequentialOnly);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.Length > maximumBytes - written)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.CanonicalPayloadExceeded); }
        written += buffer.Length;
    }
}
