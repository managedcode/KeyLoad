namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineOwnerShutdown
{
    internal static async Task CleanupSessionAsync(NativeTextOnlineSession session,
        NativeTextOnlineRoot root, NativeTextOnlineSessions sessions)
    {
        await session.CloseCommandAdmissionAndJoinAsync().ConfigureAwait(false);
        if (session.Publication.CapturedOriginal() is not null && !session.CatalogCommitted)
        {
            throw NativeTextErrors.Ownership();
        }
        session.DisposeWithJoinedNativeCleanup(() =>
        {
            if (session.CatalogCommitted)
            {
                return;
            }
            if (session.Leaf is { } leaf && session.Scope is { } scope)
            {
                root.DeleteAfterJoinedOwnership(leaf, scope);
            }
            session.GenerationReservation?.CompleteAfterJoinedCleanup();
            session.GenerationReservation = null;
        });
        sessions.RemoveAfterJoinedCleanup(session);
    }

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
