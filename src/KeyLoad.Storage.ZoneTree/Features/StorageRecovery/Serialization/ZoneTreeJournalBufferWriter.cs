using System.Buffers;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeJournalBufferWriter(int maxFrameBytes) : IBufferWriter<byte>, IDisposable
{
    private const int UnspecifiedSizeHint = 0;
    private const int NoRemainingBytes = 0;
    private const int MinimumSegmentBytes = 1;

    private PooledBuffer _buffer = new();

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > maxFrameBytes - _buffer.Length)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.EncodedTransactionFrameLimitExceeded);
        }

        _buffer.Advance(count);
    }

    public Memory<byte> GetMemory(int sizeHint = UnspecifiedSizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var memory = _buffer.GetMemory(sizeHint);
        return memory[..GetAvailableLength(memory.Length, sizeHint)];
    }

    public Span<byte> GetSpan(int sizeHint = UnspecifiedSizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var span = _buffer.GetSpan(sizeHint);
        return span[..GetAvailableLength(span.Length, sizeHint)];
    }

    internal byte[] ToArray() => _buffer.ToArray();

    public void Dispose() => _buffer.Dispose();

    private int GetAvailableLength(int allocatedLength, int sizeHint)
    {
        // Orleans' pooled segments and raw-byte writer bound temporary segment requests.
        // Honour contiguous hints even beyond the remaining budget; only Advance publishes
        // bytes. Trimming pooled capacity prevents unused pool rounding from extending work.
        var remaining = Math.Max(NoRemainingBytes, maxFrameBytes - _buffer.Length);
        var requested = Math.Max(MinimumSegmentBytes, sizeHint);
        return Math.Min(allocatedLength, Math.Max(remaining, requested));
    }
}
