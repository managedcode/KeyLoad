namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireBoundedStream(Stream target, long maximum) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => target.Length;
    public override long Position
    {
        get => target.Position; set => throw new NotSupportedException();
    }
    public override void Flush() => target.Flush();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > maximum - target.Length)
        {
            throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence);
        }
        target.Write(buffer);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
