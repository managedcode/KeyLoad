namespace KeyLoad.CrashHost;

// Counts the exact canonical UTF-8 serializer output without retaining that output.
internal sealed class SampleChunkSnapshotCountingStream(int maximumBytes) : Stream
{
    private long length;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => length;
    public override long Position { get => length; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var next = checked(length + buffer.Length);
        if (next > maximumBytes)
        { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        length = next;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
