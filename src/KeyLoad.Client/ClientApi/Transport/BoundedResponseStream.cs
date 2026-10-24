namespace KeyLoad.Client.ClientApi;

/// <summary>Admits one finite JSON response without owning or buffering its original body.</summary>
internal sealed class BoundedResponseStream(Stream body, int maximumBytes) : Stream
{
    private const string Excess = "The SDK response exceeded its configured byte limit.";
    private int consumed;

    internal static void RequireLength(HttpContent content, int maximumBytes)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new IOException(Excess);
        }
    }

    public override bool CanRead => body.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = body.Read(buffer[..Allowed(buffer.Length)]);
        Charge(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await body.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken).ConfigureAwait(false);
        Charge(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Allowed(int requested) => Math.Min(requested, maximumBytes - consumed + 1);

    private void Charge(int read)
    {
        consumed += read;
        if (consumed > maximumBytes)
        {
            throw new IOException(Excess);
        }
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
