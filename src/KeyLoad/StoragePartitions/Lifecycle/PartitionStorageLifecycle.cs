namespace KeyLoad.Server;

/// <summary>Closes storage after Orleans has joined its execution request and activation lifetimes.</summary>
internal sealed class PartitionStorageLifecycle(PartitionHostApplicationOwner owner)
    : ILifecycleParticipant<ISiloLifecycle>
{
    private const string LifecycleName = "KeyLoad.PartitionStorage";
    private const int NoOriginalFailures = 0;
    private const int AfterRequestDrainStage = ServiceLifecycleStage.RuntimeStorageServices - 1;
    private readonly Lock gate = new();
    private Task? closing;

    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(LifecycleName, AfterRequestDrainStage, _ => Task.CompletedTask, StopAsync);
    }

    internal Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (gate)
        {
            return closing ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        // Retain the original close before cancellation callbacks can synchronously reenter.
        await Task.Yield();
        var failures = new List<Exception>();
        // The same terminal owner joins accepted connections and requests during DI disposal.
        await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
        ThrowOriginalFailures(failures);
    }

    private static void ThrowOriginalFailures(IEnumerable<Exception> failures)
    {
        var originals = new List<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        foreach (var failure in failures)
        {
            CollectOriginal(failure, originals, seen);
        }
        ServerFailureObserver.ThrowIfAny(originals);
    }

    private static void CollectOriginal(Exception failure, List<Exception> originals, HashSet<Exception> seen)
    {
        if (!seen.Add(failure))
        {
            return;
        }
        if (failure is AggregateException aggregate && aggregate.InnerExceptions.Count > NoOriginalFailures)
        {
            foreach (var original in aggregate.InnerExceptions)
            {
                CollectOriginal(original, originals, seen);
            }
        }
        else
        {
            originals.Add(failure);
        }
    }
}
