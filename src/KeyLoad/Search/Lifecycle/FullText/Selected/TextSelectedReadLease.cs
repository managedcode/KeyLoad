namespace KeyLoad.Server.Search;

internal sealed class TextSelectedReadLease(TextSelectedReadAdmission admission,
    TextResourceReservation? reservation = null) : IDisposable
{
    private TextSelectedReadAdmission? owner = admission;
    internal void RetainFailure(Exception actual) =>
        (owner ?? throw new ObjectDisposedException(nameof(TextSelectedReadLease))).RetainFailure(actual);
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.ExitRead(reservation);
}
