using System.Collections.Concurrent;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Retains actual native tasks; signals do not replace any producer.</summary>
internal sealed class DeclaredResponseEofTasks
{
    private readonly ConcurrentQueue<Task> originals = new();
    private readonly HashSet<Task> observed = new();

    internal T Retain<T>(T actual) where T : Task
    {
        originals.Enqueue(actual);
        return actual;
    }

    internal async Task<T> CompleteAsync<T>(Task<T> actual)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => actual, failures);
        observed.Add(actual);
        ServerFailureObserver.ThrowIfAny(failures);
        return await actual.ConfigureAwait(false);
    }

    internal async Task JoinAsync(Task actual)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => actual, failures);
        observed.Add(actual);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task JoinRemainingAsync(List<Exception> failures)
    {
        foreach (var actual in originals)
        {
            if (observed.Add(actual))
            {
                await ServerFailureObserver.ObserveAsync(() => actual, failures);
            }
        }
    }
}
