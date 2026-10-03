using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheFileFixture : IDisposable
{
    private const string CleanupFailure = "Real coordinated ZoneTree fixture cleanup failed.";
    private const string ReopenFailure = "The disposed store is not owned by this fixture.";
    private const string DirectoryPrefix = "keyload-coordinated-cache-";
    private const string DirectoryTokenFormat = "N";
    private const long OwnerByteLimit = 32 * 1024;
    private readonly List<(string Directory, ZoneTreeStore Store)> stores = [];
    private readonly List<string> directories = [];
    private int disposeStarted;

    internal CacheMemoryBudget Budget { get; }

    internal ZoneTreeCoordinatedPointCacheFileFixture(CacheMemoryLimits? limits = null)
        => Budget = new CacheMemoryBudget(limits ?? new CacheMemoryLimits());

    internal ZoneTreeStore OpenStore(int maxEntries = 8, bool embedded = false,
        Action<CommitStage, long, int>? faultObserver = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeStarted) != 0, this);
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(DirectoryTokenFormat));
        directories.Add(directory);
        var options = new ZoneTreeStoreOptions(directory) { FaultObserver = faultObserver };
        if (embedded)
        {
            options = options with { EmbeddedPointCache = CreateOptions(maxEntries) };
        }

        var store = new ZoneTreeStore(options);
        stores.Add((directory, store));
        return store;
    }

    internal ZoneTreePointCacheOptions CreateOptions(int maxEntries = 8)
        => new(Budget)
        {
            MaxEntries = maxEntries,
            MaxRetainedBytes = OwnerByteLimit
        };

    internal void CloseStore(ZoneTreeStore store)
    {
        var index = stores.FindIndex(entry => ReferenceEquals(entry.Store, store));
        if (index < 0)
        {
            return;
        }

        stores[index].Store.Dispose();
        stores.RemoveAt(index);
    }

    internal ZoneTreeStore ReopenStoreAtSameDirectory(ZoneTreeStore closedStore)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeStarted) != 0, this);
        var index = stores.FindIndex(entry => ReferenceEquals(entry.Store, closedStore));
        if (index < 0)
        {
            throw new InvalidOperationException(ReopenFailure);
        }

        var directory = stores[index].Directory;
        var reopened = new ZoneTreeStore(new ZoneTreeStoreOptions(directory));
        stores[index] = (directory, reopened);
        return reopened;
    }

    internal async Task RunAsync(Func<Task> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var failures = new List<Exception>();
        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(scenario, failures);
        }
        finally
        {
            CaptureCleanup(Dispose, failures);
        }

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        foreach (var entry in stores)
        {
            CaptureCleanup(entry.Store.Dispose, failures);
        }

        stores.Clear();
        foreach (var directory in directories)
        {
            CaptureCleanup(() => DeleteDirectory(directory), failures);
        }
        CaptureCleanup(Budget.Dispose, failures);

        if (failures.Count != 0)
        {
            throw new AggregateException(CleanupFailure, failures);
        }
    }

    private static void CaptureCleanup(Action cleanup, List<Exception> failures)
    {
        try
        {
            CleanupOrWrapFailure(cleanup);
        }
        catch (AggregateException exception)
        {
            failures.AddRange(exception.Flatten().InnerExceptions);
        }
    }

    private static void CleanupOrWrapFailure(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            throw new AggregateException(CleanupFailure, exception);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
