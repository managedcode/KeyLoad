using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Authorization;

/// <summary>Owns only these original row-revision fixtures and their actual cold store close observations.</summary>
internal sealed class RowRevisionInferenceLifetime(TestDatabase original)
{
    private readonly List<ZoneTreeStore> owners = [];
    private bool initialAttempted;
    private bool initialClosed;
    private bool uncertain;
    internal bool CanOpenCold => initialClosed && !uncertain && owners.Count == 0;

    internal bool CloseOriginal(List<Exception> failures)
    {
        if (!initialAttempted)
        {
            initialAttempted = true;
            ServerFailureObserver.Observe(() =>
            {
                original.JoinOwnedResources();
                initialClosed = true;
            }, failures);
            if (!initialClosed)
            {
                uncertain = true;
            }
        }
        return initialClosed;
    }

    internal ZoneTreeStore? OpenCold(StoreIdentity identity, List<Exception> failures)
    {
        ZoneTreeStore? store = null;
        var recorded = false;
        ServerFailureObserver.Observe(() =>
        {
            store = ZoneTreeExistingStore.Open(new(original.Directory)
            {
                Incarnation = identity.Incarnation
            }, identity.NodeId, original.StorageExecution);
            owners.Add(store);
            recorded = true;
        }, failures);
        if (!recorded)
        {
            uncertain = true;
        }
        return store;
    }

    internal bool CloseCold(ZoneTreeStore store, List<Exception> failures)
    {
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            store.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            owners.Remove(store);
        }
        else
        {
            uncertain = true;
        }
        return closed;
    }

    internal void Cleanup(List<Exception> failures)
    {
        CloseOriginal(failures);
        if (CanOpenCold)
        {
            ServerFailureObserver.Observe(() =>
            {
                if (Directory.Exists(original.Directory))
                {
                    Directory.Delete(original.Directory, recursive: true);
                }
            }, failures);
        }
    }
}
