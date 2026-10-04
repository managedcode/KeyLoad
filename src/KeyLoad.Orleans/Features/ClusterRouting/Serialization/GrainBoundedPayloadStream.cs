namespace KeyLoad.Orleans;

internal sealed class GrainBoundedPayloadStream(int maximumBytes, CancellationToken cancellationToken) : Stream
{
    private const int InitialCapacity = 4_096;
    private const int GrowthFactor = 2;
    private readonly MemoryStream buffer = new(Math.Min(InitialCapacity, maximumBytes));

    /// <inheritdoc />
    public override bool CanRead => false;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => true;
    /// <inheritdoc />
    public override long Length => buffer.Length;
    /// <inheritdoc />
    public override long Position { get => buffer.Position; set => throw new NotSupportedException(); }

    internal byte[] Complete()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }

    /// <inheritdoc />
    public override void Write(byte[] bytes, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Write(bytes.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > maximumBytes - buffer.Length)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        var required = checked((int)buffer.Length + bytes.Length);
        if (buffer.Capacity < required)
        {
            buffer.Capacity = Math.Min(maximumBytes, Math.Max(required, checked(buffer.Capacity * GrowthFactor)));
        }

        buffer.Write(bytes);
    }

    /// <inheritdoc />
    public override void Flush() => cancellationToken.ThrowIfCancellationRequested();
    /// <inheritdoc />
    public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            buffer.Dispose();
        }

        base.Dispose(disposing);
    }
}
