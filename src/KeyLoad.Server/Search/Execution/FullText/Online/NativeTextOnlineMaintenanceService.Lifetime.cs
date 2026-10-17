namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineMaintenanceService
{
    public async Task AbortAsync(Guid sessionId)
    {
        var original = sessions.FindForCleanup(sessionId);
        if (original is null)
        {
            return;
        }
        _ = await worker.RunAsync(async () =>
        {
            await CleanupSessionAsync(original).ConfigureAwait(false);
            return original.Id;
        }).ConfigureAwait(false);
    }

    private Task CleanupSessionAsync(NativeTextOnlineSession session)
        => NativeTextOnlineOwnerShutdown.CleanupSessionAsync(session, root, sessions);

    private async Task DisposeCoreAsync()
    {
        var originals = sessions.CloseAndCapture();
        var originalReaders = catalog.OnlineReadAdmission.CloseAndJoinReaders();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => worker.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await NativeTextOnlineOwnerShutdown.CompleteAsync(originals, originalReaders, catalog, shutdown, CleanupSessionAsync, failures).ConfigureAwait(false);
    }
}
