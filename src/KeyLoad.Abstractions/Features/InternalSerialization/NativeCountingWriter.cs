using System.Buffers;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class NativeCountingWriter : IBufferWriter<byte>, IDisposable
{
    private byte[]? buffer;
    internal long Length { get; private set; }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer?.Length ?? 0);
        Length = checked(Length + count);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return buffer;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return buffer;
    }

    private void EnsureCapacity(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var required = Math.Max(sizeHint, 1);
        if (buffer is { } existing && existing.Length >= required)
        {
            return;
        }
        Dispose();
        buffer = ArrayPool<byte>.Shared.Rent(required);
    }

    public void Dispose()
    {
        if (buffer is { } owned)
        {
            buffer = null;
            ArrayPool<byte>.Shared.Return(owned, clearArray: true);
        }
    }
}
