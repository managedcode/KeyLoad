namespace KeyLoad.Server.Features.Search;

internal sealed class AnnReadReservations
{
    private const long Empty = 0;
    private readonly Lock gate = new();
    private long reserved;

    internal long Bytes
    {
        get
        {
            lock (gate)
            {
                return reserved;
            }
        }
    }

    internal AnnReadReservation Reserve(long bytes)
    {
        if (bytes <= Empty)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
        }
        lock (gate)
        {
            var next = checked(reserved + bytes);
            var lease = new AnnReadReservation(this, bytes);
            reserved = next;
            return lease;
        }
    }

    internal void Release(long bytes)
    {
        lock (gate)
        {
            if (bytes <= Empty || bytes > reserved)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
            reserved -= bytes;
        }
    }
}
