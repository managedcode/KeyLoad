using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Joins the actual maintenance owner while retaining the initiating assertion/operation and cleanup failures.</summary>
internal static class NativeTextIncrementalIntentOwner
{
    internal static async Task RunAsync(TestDatabase database, Func<NativeTextMaintenanceTestRuntime, Task> operation)
    {
        NativeTextMaintenanceTestRuntime? runtime = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                runtime = new NativeTextMaintenanceTestRuntime(database);
                await operation(runtime);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            { await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
