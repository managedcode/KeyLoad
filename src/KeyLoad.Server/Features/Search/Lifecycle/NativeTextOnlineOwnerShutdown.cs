namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineOwnerShutdown
{
    internal static async Task CompleteAsync(NativeTextOnlineSession[] originals, Task originalReaders, NativeTextOnlineCatalogOwner catalog,
        CancellationTokenSource shutdown, Func<NativeTextOnlineSession, Task> CleanupSessionAsync, List<Exception> failures)
    {
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => CleanupSessionAsync(original), failures).ConfigureAwait(false);
        }
        foreach (var originalRetirement in catalog.CaptureOriginalRetirements())
        {
            await ServerFailureObserver.ObserveAsync(() => originalRetirement, failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(catalog.OnlineReadAdmission.ThrowRetained, failures);
        var retained = catalog.CaptureGenerations();
        foreach (var generation in retained)
        {
            await ServerFailureObserver.ObserveAsync(generation.MarkRetirement, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(generation.CompleteAfterJoinedRetirement, failures);
        }
        try
        {
            shutdown.Dispose();
        }
        catch (Exception cleanup)
        {
            failures.Add(cleanup);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
