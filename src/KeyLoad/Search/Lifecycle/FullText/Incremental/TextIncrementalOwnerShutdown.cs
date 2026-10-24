namespace KeyLoad.Server.Search;

internal static class TextIncrementalOwnerShutdown
{
    internal static async Task RunAsync(TextIncrementalSessions sessions, TextSelectedReadAdmission selectedAdmission,
        Task originalWorker, Task originalReaders, Lock selectedGate, Dictionary<string, TextSelectedIndexSlot> selectedSlots)
    {
        var failures = new List<Exception>();
        try
        {
            await originalWorker;
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures);
        ServerFailureObserver.Observe(selectedAdmission.ThrowRetained, failures);
        TextSelectedIndexSlot[] originals;
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
