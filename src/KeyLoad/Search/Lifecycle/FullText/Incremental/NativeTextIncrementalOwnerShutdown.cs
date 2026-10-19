namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalOwnerShutdown
{
    internal static async Task RunAsync(NativeTextIncrementalSessions sessions, NativeTextSelectedReadAdmission selectedAdmission,
        Task originalWorker, Lock selectedGate, Dictionary<string, NativeTextSelectedIndexSlot> selectedSlots)
    {
        sessions.CloseAdmission();
        var originalReaders = selectedAdmission.CloseAndJoinReaders();
        var failures = new List<Exception>();
        try
        {
            await originalWorker.ConfigureAwait(false);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(selectedAdmission.ThrowRetained, failures);
        NativeTextSelectedIndexSlot[] originals;
        lock (selectedGate)
        {
            originals = selectedSlots.Values.ToArray();
        }
        foreach (var original in originals)
        {
            ServerFailureObserver.Observe(original.Dispose, failures);
        }
        ServerFailureObserver.Observe(sessions.DisposeAfterWorkerJoin, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
