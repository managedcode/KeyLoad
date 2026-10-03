using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireConcurrencySupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireConcurrentTaskOwner
{
    internal Task<ConcurrentResult>?[] Workers { get; } = new Task<ConcurrentResult>?[WorkerCount];
    internal Task<ConcurrentResult[]>? WorkerJoin { get; set; }
    internal Task? ScenarioTask { get; set; }
    internal Task? DisposeTask { get; set; }
    internal Task? FallbackTask { get; set; }
    internal Task? FallbackDispatchTask { get; set; }
    internal Task[] Originals { get; private set; } = [];
    internal Task? OriginalJoin { get; private set; }
    internal Task? Cleanup { get; private set; }
    internal Task? CleanupObservation { get; private set; }

    internal void RegisterCleanup(CacheControlAuthenticator authenticator)
    {
        WorkerJoin ??= Task.WhenAll(Workers.Where(task => task is not null).Select(task => task!));
        var originals = Workers.Where(task => task is not null).Cast<Task>().ToList();
        originals.Add(WorkerJoin);
        AddIfPresent(originals, ScenarioTask);
        AddIfPresent(originals, DisposeTask);
        AddIfPresent(originals, FallbackTask);
        AddIfPresent(originals, FallbackDispatchTask);
        Originals = [.. originals];
        OriginalJoin = Task.WhenAll(Originals);
        Cleanup = ReleaseAfterOriginalsAsync(authenticator);
        CleanupObservation = Cleanup.ContinueWith(static completed => ObserveTerminal(completed),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ReleaseAfterOriginalsAsync(CacheControlAuthenticator authenticator)
    {
        try
        {
            await OriginalJoin!.ConfigureAwait(false);
        }
        finally
        {
            // This retained lifetime owns the signer even after the caller's finite wait fails.
            // It never asserts or mutates the returned failure collection.
            foreach (var original in Originals)
            {
                ObserveTerminal(original);
            }

            ObserveTerminal(OriginalJoin!);
            authenticator.Dispose();
        }
    }

    private static void ObserveTerminal(Task task)
    {
        if (task.IsFaulted)
        {
            _ = task.Exception;
        }
        else
        {
            _ = task.IsCanceled;
        }
    }

    private static void AddIfPresent(List<Task> tasks, Task? task)
    {
        if (task is not null)
        {
            tasks.Add(task);
        }
    }
}
