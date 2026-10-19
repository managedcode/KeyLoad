using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.ResourceExecution.CachePolicyEpochProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CachePolicyEpochOwner : IDisposable
{
    private readonly List<StorageOwnerSettlement> attempts = [];
    private readonly Func<ZoneTreeStore, DatabaseEngine> createDatabase;
    private ZoneTreeStore? store;
    private bool closeAttempted;
    private bool disposed;
    private bool retain;
    internal string Directory
    {
        get;
    } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "keyload-cache-policy-" + Guid.NewGuid().ToString("N"));
    internal CacheMemoryBudget Budget
    {
        get;
    } = new(UnitAdmissionOptions.Cache(new()
    {
        MaxRetainedBytes = P.PoolBytes,
        MaxRetainedEntries = P.PoolEntries
    }));
    internal ZoneTreeStore Store => store ?? throw new InvalidOperationException(P.UnknownOwner);
    internal DatabaseEngine Database
    {
        get;
        private set;
    } = null!;
    internal ZoneTreePointCacheSnapshot OpeningCache
    {
        get;
        private set;
    }

    internal CachePolicyEpochOwner()
    {
        createDatabase = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.RuntimeClaimsExecution(), null, TimeProvider.System);
    }

    internal void Open(bool first)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (store is not null || attempts.Any(attempt => !attempt.IsReleased))
        {
            throw new InvalidOperationException(P.UnknownOwner);
        }
        var attempt = new StorageOwnerSettlement();
        attempts.Add(attempt);
        closeAttempted = false;
        store = ZoneTreeStore.OpenOwned(new(Directory)
        {
            EmbeddedPointCache = new(Budget)
            {
                MaxEntries = P.Entries,
                MaxRetainedBytes = P.OwnerBytes,
                MaxKeyBytes = P.MaxKeyBytes,
                MaxValueBytes = P.MaxValueBytes
            }
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution(), attempt);
        OpeningCache = store.GetPointCacheDiagnostics();
        Database = createDatabase(store);
        if (first)
        {
            TestDatabaseEngineFactory.Bootstrap(Database, P.Root, P.RootSecret);
        }
    }

    internal void Close()
    {
        if (store is null || closeAttempted)
        {
            return;
        }
        closeAttempted = true;
        store.Dispose();
        store = null;
    }

    internal void Retain() => retain = true;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Close, failures);
        ServerFailureObserver.Observe(Budget.Dispose, failures);
        if (!retain && store is null && attempts.All(attempt => attempt.IsReleased) && failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => System.IO.Directory.Delete(Directory, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
