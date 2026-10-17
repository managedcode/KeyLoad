namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    private async Task StopCoreAsync(Task registered, Task? preparing, Task? starting)
    {
        await registered.ConfigureAwait(false);
        var failures = new List<Exception>();
        var cleanupFailures = new List<Exception>();
        await requestLifetime.CancelAndJoinStartupAsync(preparing, starting, failures, cleanupFailures).ConfigureAwait(false);
        await localRuntime.JoinResourcesAsync(requestLifetime, databaseBootstrap, physicalOwnerWork,
            () => Volatile.Write(ref grains, null), cleanupFailures).ConfigureAwait(false);
        closure.Complete(cleanupFailures);
        failures.AddRange(cleanupFailures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
