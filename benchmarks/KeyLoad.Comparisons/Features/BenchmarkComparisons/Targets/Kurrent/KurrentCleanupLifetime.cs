using KurrentDB.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentCleanupLifetime
{
    internal static Task[] StartDisposals(KurrentCleanupState state,
        IReadOnlyList<KurrentDBClient> nativeClients, IReadOnlyList<HttpClient> httpClients)
    {
        var disposals = new List<Task>(nativeClients.Count + httpClients.Count);
        foreach (var client in nativeClients)
        {
            disposals.Add(Task.Run(() => DisposeNativeAsync(state, client), CancellationToken.None));
        }
        foreach (var client in httpClients)
        {
            disposals.Add(Task.Run(() => DisposeHttp(state, client), CancellationToken.None));
        }
        return disposals.ToArray();
    }

    internal static async Task DrainAsync(KurrentCleanupState state, Task[] workers, KurrentCleanupCancellation cancellation,
        Task[] disposals, Task writerDisposal)
    {
        var originalTasks = workers.Concat(disposals).Append(writerDisposal).Append(ObserveWriterAsync(state, writerDisposal))
            .Append(state.CancellationCallbacks).ToArray();
        var joined = Task.WhenAll(originalTasks);
        cancellation.CloseAfter(joined);
        try
        {
            RequireWithinBudget(state);
            var remaining = state.Remaining;
            await joined.WaitAsync(joined.IsCompleted ? TimeSpan.Zero : remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            RequireWithinBudget(state);
        }
        catch (Exception error)
        {
            state.RecordFailure(error, KurrentCleanupStage.Drain);
            Observe(joined);
            foreach (var task in originalTasks)
            {
                Observe(task);
            }
            throw;
        }

    }

    private static void RequireWithinBudget(KurrentCleanupState state)
    {
        if (state.Remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException(KurrentConstants.CleanupDrainFailed);
        }
    }

    private static async Task ObserveWriterAsync(KurrentCleanupState state, Task writerDisposal)
    {
        try
        {
            await writerDisposal;
        }
        catch (Exception error)
        {
            state.RecordFailure(error, KurrentCleanupStage.NativeDispose, disposal: true);
            throw;
        }
    }

    private static async Task DisposeNativeAsync(KurrentCleanupState state, KurrentDBClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception error)
        {
            state.RecordFailure(error, KurrentCleanupStage.NativeDispose, disposal: true);
            throw;
        }
    }

    private static void DisposeHttp(KurrentCleanupState state, HttpClient client)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception error)
        {
            state.RecordFailure(error, KurrentCleanupStage.HttpDispose, disposal: true);
            throw;
        }
    }

    private static void Observe(Task task)
        => _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
