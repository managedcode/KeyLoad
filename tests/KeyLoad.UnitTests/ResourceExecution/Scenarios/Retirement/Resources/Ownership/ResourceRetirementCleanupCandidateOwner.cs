using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Owns real nested provider stores inside the original retained test root.</summary>
internal static class ResourceRetirementCleanupCandidateOwner
{
    private const string OwnerKey = "KeyLoad.CleanupCandidate.Owner";
    private const string LifetimeKey = "KeyLoad.CleanupCandidate.OwnerLifetime";
    private const string DirectoryKey = "KeyLoad.CleanupCandidate.Directory";

    internal static async Task RunAsync(TestDatabase fixture, string name, int frameBytes, Func<ZoneTreeStore, Task> operation)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        var directory = Path.Combine(fixture.Directory, name);
        var store = lifetime.Acquire(() => new ZoneTreeStore(new(directory)
        {
            MaxFrameBytes = frameBytes
        },
            fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => operation(store), failures);
            lifetime.Close(store, failures);
        }
        foreach (var failure in failures.ToArray())
        {
            ServerFailureObserver.Observe(() =>
            {
                failure.Data[OwnerKey] = store;
                failure.Data[LifetimeKey] = lifetime;
                failure.Data[DirectoryKey] = directory;
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
