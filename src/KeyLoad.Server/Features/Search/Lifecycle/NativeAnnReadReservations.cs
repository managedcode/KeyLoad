namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnReadReservations
{
    private const long Empty = 0;
    private readonly Lock gate = new();
    private long reserved;

    internal long Bytes
    {
        get { lock (gate) { return reserved; } }
    }

    internal NativeAnnReadReservation Reserve(long bytes)
    {
        if (bytes <= Empty)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound); }
        lock (gate)
        {
            var next = checked(reserved + bytes);
            var lease = new NativeAnnReadReservation(this, bytes);
            reserved = next;
            return lease;
        }
    }

    internal void Release(long bytes)
    {
        lock (gate)
        {
            if (bytes <= Empty || bytes > reserved)
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
            reserved -= bytes;
        }
    }
}
