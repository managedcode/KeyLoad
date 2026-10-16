using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingFixture
{
    internal static async Task RunAsync(ResourcePairBindingFault fault, CancellationToken token)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        var source = lifetime.Acquire(() => new TestDatabase(directory: directory, nativeReplicaAdmission: true,
            nativeReplicaDirectory: directory + TestDatabaseReplicaDirectory.Suffix), failures);
        if (source is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => ResourcePairBindingFlow.RunAsync(new(source, fault), token), failures);
            ServerFailureObserver.Observe(source.JoinOwnedResources, failures);
            if (failures.Count == Zero)
            {
                lifetime.Close(source, failures);
                if (lifetime.CanAdvance)
                {
                    ServerFailureObserver.Observe(() => Directory.Delete(directory + TestDatabaseReplicaDirectory.Suffix, true), failures);
                }
            }
        }
        foreach (var failure in failures)
        {
            failure.Data[RootKey] = directory;
            failure.Data[ReplicaRootKey] = directory + TestDatabaseReplicaDirectory.Suffix;
            failure.Data[OwnerKey] = source;
            failure.Data[LifetimeKey] = lifetime;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
