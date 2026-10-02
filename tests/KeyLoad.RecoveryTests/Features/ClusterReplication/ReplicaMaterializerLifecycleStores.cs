using System.Runtime.ExceptionServices;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaMaterializerLifecycleStores(ZoneTreeStore canonical, ZoneTreeStore replica) : IDisposable
{
    private bool disposed;
    internal ZoneTreeStore Canonical { get; } = canonical;
    internal ZoneTreeStore Replica { get; } = replica;

    internal static ReplicaMaterializerLifecycleStores Open(Func<ZoneTreeStore> openCanonical,
        Func<ZoneTreeStore> openReplica)
    {
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        try
        {
            canonical = openCanonical();
            replica = openReplica();
            var owners = new ReplicaMaterializerLifecycleStores(canonical, replica);
            canonical = null;
            replica = null;
            return owners;
        }
        finally
        {
            replica?.Dispose();
            canonical?.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        List<Exception> failures = [];
        DisposeStore(Replica, failures);
        DisposeStore(Canonical, failures);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static void DisposeStore(ZoneTreeStore store, List<Exception> failures)
    {
        try
        { store.Dispose(); }
        catch (IOException error) { failures.Add(error); }
        catch (UnauthorizedAccessException error) { failures.Add(error); }
        catch (ObjectDisposedException error) { failures.Add(error); }
        catch (InvalidOperationException error) { failures.Add(error); }
        catch (KeyLoadException error) { failures.Add(error); }
    }
}
