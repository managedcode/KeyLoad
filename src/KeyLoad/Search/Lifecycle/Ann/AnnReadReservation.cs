namespace KeyLoad.Server.Features.Search;

internal sealed class AnnReadReservation(AnnReadReservations owner, long bytes) : IDisposable
{
    private AnnReadReservations? current = owner;
    internal long Bytes { get; } = bytes;
    public void Dispose() => Interlocked.Exchange(ref current, null)?.Release(Bytes);
}
