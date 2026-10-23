using System.Buffers;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class EventFeedPubSubCountingWriter(int maximum,
    IOptions<GrainRoutingOptions> routing, CancellationToken cancellationToken) : IBufferWriter<byte>, IDisposable
{
    private readonly GrainCountingWriter original = new(maximum, routing, cancellationToken);
    internal long Length => original.Length;
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        try
        {
            return original.GetMemory(sizeHint);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, EventFeedPubSubProtocol.Capacity, error);
        }
    }
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        try
        {
            return original.GetSpan(sizeHint);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, EventFeedPubSubProtocol.Capacity, error);
        }
    }
    public void Advance(int count)
    {
        try
        {
            original.Advance(count);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, EventFeedPubSubProtocol.Capacity, error);
        }
    }
    public void Dispose() => original.Dispose();
}
