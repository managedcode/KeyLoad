namespace KeyLoad.Server.Features.Search;

internal static class TextIncrementalShutdown
{
    internal static async Task JoinAsync(Func<Task> stopOriginalWorker,
        TextIncrementalSessions sessions)
    {
        sessions.CloseAdmission();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(stopOriginalWorker, failures);
        ServerFailureObserver.Observe(sessions.DisposeAfterWorkerJoin, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
