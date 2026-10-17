using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class CoordinatorShutdownStorage
{
    private bool originalAttempted;
    private bool originalClosed;
    private bool uncertain;
    private ZoneTreeStore? cold;
    internal bool CanOpen => originalClosed && !uncertain && cold is null;
    internal void RetainRoot() => uncertain = true;

    internal void CloseOriginal(TestDatabase db, List<Exception> failures)
    {
        if (originalAttempted)
        {
            return;
        }
        originalAttempted = true;
        ServerFailureObserver.Observe(() =>
        {
            db.JoinOwnedResources();
            originalClosed = true;
        }, failures);
        uncertain |= !originalClosed;
    }

    internal ZoneTreeStore? Open(TestDatabase db, StoreIdentity identity, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => cold = ZoneTreeExistingStore.Open(new(db.Directory)
        {
            Incarnation = identity.Incarnation
        }, identity.NodeId, db.StorageExecution), failures);
        uncertain |= cold is null;
        return cold;
    }

    internal void CloseCold(List<Exception> failures)
    {
        if (cold is null)
        {
            return;
        }
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            cold.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            cold = null;
        }
        else
        {
            uncertain = true;
        }
    }

    internal void Cleanup(TestDatabase db, List<Exception> failures)
    {
        CloseOriginal(db, failures);
        if (CanOpen)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(db.Directory, recursive: true), failures);
        }
    }
}
