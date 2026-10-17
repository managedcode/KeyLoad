namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextSelectedReadLease(NativeTextSelectedReadAdmission admission,
    NativeTextResourceReservation? reservation = null) : IDisposable
{
    private NativeTextSelectedReadAdmission? owner = admission;
    internal void RetainFailure(Exception actual) =>
        (owner ?? throw new ObjectDisposedException(nameof(NativeTextSelectedReadLease))).RetainFailure(actual);
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.ExitRead(reservation);
}
