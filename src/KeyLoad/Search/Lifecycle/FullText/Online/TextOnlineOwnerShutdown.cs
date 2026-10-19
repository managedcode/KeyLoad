namespace KeyLoad.Server.Features.Search;

internal static class TextOnlineOwnerShutdown
{
    internal static async Task CleanupSessionAsync(TextOnlineSession session,
        TextOnlineRoot root, TextOnlineSessions sessions)
    {
        await session.CloseCommandAdmissionAndJoinAsync();
        if (session.Publication.CapturedOriginal() is not null && !session.CatalogCommitted)
        {
            throw TextErrors.Ownership();
        }
        session.DisposeWithJoinedCleanup(() =>
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

    internal static async Task CompleteAsync(TextOnlineSession[] originals, Task originalReaders, TextOnlineCatalogOwner catalog,
        CancellationTokenSource shutdown, Func<TextOnlineSession, Task> CleanupSessionAsync, List<Exception> failures)
    {
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => CleanupSessionAsync(original), failures);
        }
        foreach (var originalRetirement in catalog.CaptureOriginalRetirements())
        {
            await ServerFailureObserver.ObserveAsync(() => originalRetirement, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures);
        ServerFailureObserver.Observe(catalog.OnlineReadAdmission.ThrowRetained, failures);
        var retained = catalog.CaptureGenerations();
        foreach (var generation in retained)
        {
            await ServerFailureObserver.ObserveAsync(generation.MarkRetirement, failures);
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
