using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

/// <summary>Joins the actual maintenance owner while retaining the initiating assertion/operation and cleanup failures.</summary>
internal static class TextIncrementalIntentOwner
{
    internal static async Task RunAsync(TestDatabase database, Func<TextMaintenanceTestRuntime, Task> operation)
    {
        TextMaintenanceTestRuntime? runtime = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                runtime = new TextMaintenanceTestRuntime(database);
                await operation(runtime);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
