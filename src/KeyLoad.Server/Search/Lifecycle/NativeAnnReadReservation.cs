namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnReadReservation(NativeAnnReadReservations owner, long bytes) : IDisposable
{
    private NativeAnnReadReservations? current = owner;
    internal long Bytes { get; } = bytes;
    public void Dispose() => Interlocked.Exchange(ref current, null)?.Release(Bytes);
}
