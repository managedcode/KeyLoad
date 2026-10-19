using System.Buffers;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class DomBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int UnspecifiedSizeHint = 0;
    private const int WriterGrowthAllowance = 4_096;
    private readonly ArrayBufferWriter<byte> buffer = new();

    internal ReadOnlySpan<byte> WrittenSpan => buffer.WrittenSpan;

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > SerializationLimits.MaximumDomBytes - buffer.WrittenCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        buffer.Advance(count);
    }

    public Memory<byte> GetMemory(int sizeHint = UnspecifiedSizeHint)
    {
        RequireHint(sizeHint);
        return buffer.GetMemory(sizeHint);
    }

    public Span<byte> GetSpan(int sizeHint = UnspecifiedSizeHint)
    {
        RequireHint(sizeHint);
        return buffer.GetSpan(sizeHint);
    }

    private void RequireHint(int hint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hint);
        // Utf8JsonWriter can request its next minimum block even near the final logical limit.
        // Only committed output is admissible; permit a bounded spare block for that request.
        if (hint > SerializationLimits.MaximumDomBytes - buffer.WrittenCount + WriterGrowthAllowance)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }

    public void Dispose() => buffer.Clear();
}
