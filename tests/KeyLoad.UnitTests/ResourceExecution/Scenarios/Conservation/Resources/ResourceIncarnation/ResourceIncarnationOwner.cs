using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceIncarnationOwner
{
    internal static async Task RunAsync(Func<TestDatabase, Task> operation)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        var fixture = lifetime.Acquire(() => new TestDatabase(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => operation(fixture), failures);
            ServerFailureObserver.Observe(fixture.JoinOwnedResources, failures);
            if (failures.Count == 0)
            {
                lifetime.Close(fixture, failures);
            }
            foreach (var failure in failures.ToArray())
            {
                ServerFailureObserver.Observe(() =>
                {
                    failure.Data[ResourceIncarnationLiteral.RootKey] = fixture.Directory;
                    failure.Data[ResourceIncarnationLiteral.OwnerKey] = fixture;
                    failure.Data[ResourceIncarnationLiteral.OwnerLifetimeKey] = lifetime;
                }, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
