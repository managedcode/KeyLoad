using System.Collections.Concurrent;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class SdkProblemBoundaryTasks
{
    private readonly ConcurrentQueue<Task> originals = new();
    private readonly ConcurrentDictionary<Task, byte> observed = new();
    private const byte Present = 1;
    internal T Retain<T>(T original) where T : Task
    {
        originals.Enqueue(original);
        return original;
    }
    internal async Task<T> CompleteAsync<T>(Task<T> original)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, failures);
        observed.TryAdd(original, Present);
        ServerFailureObserver.ThrowIfAny(failures);
        return await original.ConfigureAwait(false);
    }
    internal async Task JoinRemainingAsync(List<Exception> failures)
    {
        foreach (var original in originals)
        {
            if (observed.TryAdd(original, Present))
            {
                await ServerFailureObserver.ObserveAsync(() => original, failures);
            }
        }
    }
}
