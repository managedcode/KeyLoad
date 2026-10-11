using System.Text.Json;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Counts the complete native public JSON projection without retaining its bytes.</summary>
internal sealed class EventFeedTailJsonMeasurement(int maximumBytes) : Stream
{
    private const string Invalid = "The event feed public stream exceeds its configured JSON byte grant.";
    private const string Unsupported = "The event feed JSON measurement supports writes only.";
    private long bytes;
    internal long Bytes => bytes;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => bytes;
    public override long Position { get => bytes; set => throw new NotSupportedException(Unsupported); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(Unsupported);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(Unsupported);
    public override void SetLength(long value) => throw new NotSupportedException(Unsupported);
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > maximumBytes - bytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Invalid); }
        bytes += buffer.Length;
    }
    internal static long Measure<T>(T value, int maximumBytes, JsonSerializerOptions options)
    {
        using var counter = new EventFeedTailJsonMeasurement(maximumBytes);
        JsonSerializer.Serialize(counter, value, options);
        return counter.Bytes;
    }
}
