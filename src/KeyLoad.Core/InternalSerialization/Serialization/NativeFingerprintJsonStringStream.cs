using System.Text.Json;

namespace KeyLoad.Core.Features.InternalSerialization;

/// <summary>Streams Microsoft serializer output into one frozen opaque JSON identity string without retaining its expanded duplicate.</summary>
internal sealed class NativeFingerprintJsonStringStream(Utf8JsonWriter writer, int chunkBytes) : Stream
{
    private bool disposed;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() => ObjectDisposedException.ThrowIf(disposed, this);
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
        ObjectDisposedException.ThrowIf(disposed, this);
        // The phase serializer's default encoder emits ASCII; segmentation retains every original escape byte.
        while (buffer.Length > chunkBytes)
        {
            writer.WriteStringValueSegment(buffer[..chunkBytes], false);
            writer.Flush();
            buffer = buffer[chunkBytes..];
        }
        writer.WriteStringValueSegment(buffer, false);
        writer.Flush();
    }
    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }
}
