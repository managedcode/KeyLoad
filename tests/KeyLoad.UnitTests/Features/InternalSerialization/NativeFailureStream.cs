namespace KeyLoad.UnitTests.Features.InternalSerialization;

// A real Stream boundary that fails writes before retaining data.
internal sealed class NativeFailureStream(Exception failure, CancellationToken cancellation = default) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Reject();
    public override void Write(ReadOnlySpan<byte> buffer) => Reject();
    private void Reject()
    {
        cancellation.ThrowIfCancellationRequested();
        throw failure;
    }
}
