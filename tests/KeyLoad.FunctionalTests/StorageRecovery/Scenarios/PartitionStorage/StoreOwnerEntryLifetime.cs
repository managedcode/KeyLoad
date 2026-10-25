using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>Tracks actual test-owned stores; only a successful original close permits root cleanup.</summary>
internal sealed class StoreOwnerEntryLifetime
{
    private readonly List<IDisposable> owners = [];
    private bool uncertain;
    internal bool CanContinue => !uncertain;
    internal bool CanDeleteRoot => !uncertain && owners.Count == 0;

    internal void Own(IDisposable owner) => owners.Add(owner);
    internal void RetainUnknown() => uncertain = true;

    internal ZoneTreeStore? Acquire(Func<ZoneTreeStore> open, List<Exception> failures)
    {
        ZoneTreeStore? owner = null;
        var recorded = false;
        ServerFailureObserver.Observe(() =>
        {
            owner = open();
            Own(owner);
            recorded = true;
        }, failures);
        if (!recorded)
        {
            uncertain = true;
        }
        return owner;
    }

    internal bool Close(IDisposable owner, List<Exception> failures)
    {
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            owner.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            owners.Remove(owner);
        }
        else
        {
            uncertain = true;
        }
        return closed;
    }
}
