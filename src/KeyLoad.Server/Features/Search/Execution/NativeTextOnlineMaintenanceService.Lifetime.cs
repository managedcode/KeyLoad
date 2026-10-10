namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineMaintenanceService
{
    public async Task AbortAsync(Guid sessionId)
    {
        var original = sessions.FindForCleanup(sessionId);
        if (original is null)
        { return; }
        _ = await worker.RunAsync(async () =>
        {
            await CleanupSessionAsync(original).ConfigureAwait(false);
            return original.Id;
        }).ConfigureAwait(false);
    }

    private async Task CleanupSessionAsync(NativeTextOnlineSession session)
    {
        await session.CloseCommandAdmissionAndJoinAsync().ConfigureAwait(false);
        if (session.Publication.CapturedOriginal() is not null && !session.CatalogCommitted)
        { throw NativeTextErrors.Ownership(); }
        session.DisposeWithJoinedNativeCleanup(() =>
        {
            if (session.CatalogCommitted)
            { return; }
            if (session.Leaf is { } leaf && session.Scope is { } scope)
            { root.DeleteAfterJoinedOwnership(leaf, scope); }
            session.GenerationReservation?.CompleteAfterJoinedCleanup();
            session.GenerationReservation = null;
        });
        sessions.RemoveAfterJoinedCleanup(session);
    }

    private async Task DisposeCoreAsync()
    {
        var originals = sessions.CloseAndCapture();
        var originalReaders = catalog.OnlineReadAdmission.CloseAndJoinReaders();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => worker.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        foreach (var original in originals)
        { await ServerFailureObserver.ObserveAsync(() => CleanupSessionAsync(original), failures).ConfigureAwait(false); }
        foreach (var originalRetirement in catalog.CaptureOriginalRetirements())
        { await ServerFailureObserver.ObserveAsync(() => originalRetirement, failures).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(catalog.OnlineReadAdmission.ThrowRetained, failures);
        var retained = catalog.CaptureGenerations();
        foreach (var generation in retained)
        {
            await ServerFailureObserver.ObserveAsync(generation.MarkRetirement, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(generation.CompleteAfterJoinedRetirement, failures);
        }
        try
        { shutdown.Dispose(); }
        catch (Exception cleanup)
        {
            failures.Add(cleanup);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
