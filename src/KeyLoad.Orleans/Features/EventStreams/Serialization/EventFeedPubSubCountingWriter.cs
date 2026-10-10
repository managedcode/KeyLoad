using System.Buffers;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class EventFeedPubSubCountingWriter(int maximum,
    IOptions<GrainRoutingOptions> routing, CancellationToken cancellationToken) : IBufferWriter<byte>, IDisposable
{
    private const int UnspecifiedSizeHint = 0;
    private readonly GrainNativeCountingWriter original = new(maximum, routing, cancellationToken);
    internal long Length => original.Length;
    public Memory<byte> GetMemory(int sizeHint = UnspecifiedSizeHint) => original.GetMemory(sizeHint);
    public Span<byte> GetSpan(int sizeHint = UnspecifiedSizeHint) => original.GetSpan(sizeHint);
    public void Advance(int count)
    {
        try { original.Advance(count); }
        catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, EventFeedPubSubProtocol.Capacity, error); }
    }
    public void Dispose() => original.Dispose();
}
