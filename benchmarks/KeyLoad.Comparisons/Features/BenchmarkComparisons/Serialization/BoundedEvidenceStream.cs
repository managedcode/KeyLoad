namespace KeyLoad.Comparisons;

internal sealed class BoundedEvidenceStream(Stream output, int maximumBytes) : Stream
{
    private int written;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => written;
    public override long Position { get => written; set => throw new NotSupportedException(); }
    public override void Flush() => output.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        Reserve(count);
        output.Write(buffer, offset, count);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reserve(buffer.Length);
        await output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
    private void Reserve(int count)
    {
        if (count < 0 || written > maximumBytes - count)
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopEvidenceByteLimitExceeded);
        written = checked(written + count);
    }
}
