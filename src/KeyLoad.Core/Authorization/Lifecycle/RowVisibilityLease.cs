namespace KeyLoad.Core.Features.Authorization;

internal sealed class RowVisibilityLease(
    RowVisibilityCache owner, RowVisibilityEntry entry, ICacheMemoryReservation charge) : IDisposable
{
    private Exception? releaseFailure;
    private bool chargeReleased;
    private bool pinReleased;
    internal bool Contains(string id) => owner.Contains(entry, id, ref pinReleased);

    public void Dispose()
    {
        owner.Release(entry, charge, ref chargeReleased, ref pinReleased, ref releaseFailure);
    }
}
