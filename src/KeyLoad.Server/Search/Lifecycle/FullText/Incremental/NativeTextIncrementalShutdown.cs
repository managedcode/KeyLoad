namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalShutdown
{
    internal static async Task JoinAsync(Func<Task> stopOriginalWorker,
        NativeTextIncrementalSessions sessions)
    {
        sessions.CloseAdmission();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(stopOriginalWorker, failures)
            .ConfigureAwait(false);
        ServerFailureObserver.Observe(sessions.DisposeAfterWorkerJoin, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
