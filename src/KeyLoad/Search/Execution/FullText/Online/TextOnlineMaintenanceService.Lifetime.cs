namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineMaintenanceService
{
    public async Task AbortAsync(Guid sessionId)
    {
        if (sessions.FindWork(sessionId) is { } work)
        {
            await work.CancelAndJoinStagesAsync();
        }
        _ = await worker.RunCleanupAsync(async () =>
        {
            var original = sessions.FindForCleanup(sessionId);
            if (original is not null)
            {
                await CleanupSessionAsync(original);
            }
            else
            {
                sessions.RetireWork(sessionId);
            }
            return sessionId;
        });
    }

    private Task CleanupSessionAsync(TextOnlineSession session)
        => TextOnlineOwnerShutdown.CleanupSessionAsync(session, root, sessions);

    private async Task DisposeCoreAsync()
    {
        var originals = sessions.CloseAndCapture();
        var originalReaders = catalog.OnlineReadAdmission.CloseAndJoinReaders();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => worker.DisposeAsync().AsTask(), failures);
        await TextOnlineOwnerShutdown.CompleteAsync(originals, originalReaders, catalog, shutdown, CleanupSessionAsync, failures);
        sessions.DisposeUncapturedWorkAfterWorkerJoin();
    }
}
