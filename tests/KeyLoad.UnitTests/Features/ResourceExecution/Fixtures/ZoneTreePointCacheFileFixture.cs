using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheFileFixture : IDisposable
{
    private const string CleanupFailure = "Real ZoneTree fixture cleanup failed.";
    private const long OwnerByteLimit = 32 * 1024;
    private readonly List<(string Directory, ZoneTreeStore Store)> stores = [];
    private readonly List<string> directories = [];
    private int disposeStarted;

    internal CacheMemoryBudget Budget { get; }

    internal ZoneTreePointCacheFileFixture(CacheMemoryLimits? limits = null)
        => Budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(limits ?? new CacheMemoryLimits()));

    internal ZoneTreeStore OpenStore(int maxEntries = 8, int maxValueBytes = 128, int maxPinsPerEntry = 8,
        Guid? incarnation = null, byte[]? signingKey = null, int maxKeyBytes = 128)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeStarted) != 0, this);
        var directory = Path.Combine(Path.GetTempPath(), "keyload-point-cache-" + Guid.NewGuid().ToString("N"));
        directories.Add(directory);
        return OpenStoreAt(directory, maxEntries, maxValueBytes, maxPinsPerEntry, incarnation, signingKey, maxKeyBytes);
    }

    internal ZoneTreeStore ReopenStore(string directory, int maxEntries = 8, int maxValueBytes = 128)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeStarted) != 0, this);
        return OpenStoreAt(directory, maxEntries, maxValueBytes, 8);
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

    internal string DirectoryOf(ZoneTreeStore store)
        => stores.Single(entry => ReferenceEquals(entry.Store, store)).Directory;

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

    internal void DetachStore(ZoneTreeStore store)
    {
        var index = stores.FindIndex(entry => ReferenceEquals(entry.Store, store));
        if (index < 0)
        {
            throw new InvalidOperationException("The store is not owned by this fixture.");
        }

        stores.RemoveAt(index);
    }

    private ZoneTreeStore OpenStoreAt(string directory, int maxEntries, int maxValueBytes, int maxPinsPerEntry,
        Guid? incarnation = null, byte[]? signingKey = null, int maxKeyBytes = 128)
    {
        var store = new ZoneTreeStore(new ZoneTreeStoreOptions(directory)
        {
            Incarnation = incarnation,
            SigningKey = signingKey is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(signingKey),
            EmbeddedPointCache = new ZoneTreePointCacheOptions(Budget)
            {
                MaxEntries = maxEntries,
                MaxRetainedBytes = OwnerByteLimit,
                MaxKeyBytes = maxKeyBytes,
                MaxValueBytes = maxValueBytes,
                MaxPinsPerEntry = maxPinsPerEntry
            }
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        stores.Add((directory, store));
        return store;
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

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
    }

    private static void CaptureCleanup(Action cleanup, List<Exception> failures)
    {
        try
        {
            CleanupOrWrapFailure(cleanup);
        }
        catch (AggregateException exception)
        {
            failures.AddRange(exception.InnerExceptions);
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
