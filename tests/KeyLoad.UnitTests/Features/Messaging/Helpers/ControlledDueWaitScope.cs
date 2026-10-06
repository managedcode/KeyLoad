using KeyLoad.Server;

namespace KeyLoad.UnitTests;

/// <summary>Joins original due waits before disposing their fixture, retaining assertion and cleanup failures.</summary>
internal sealed class ControlledDueWaitScope(CancellationToken cancellationToken)
{
    private readonly List<Task> waits = [];
    private readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    internal CancellationToken Token => lifetime.Token;
    internal Task Track(Task wait) { waits.Add(wait); return wait; }

    internal async Task RunAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures);
        foreach (var wait in waits)
        {
            await ServerFailureObserver.ObserveAsync(() => JoinAsync(wait), failures);
        }
        ServerFailureObserver.Observe(lifetime.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task JoinAsync(Task wait)
    {
        try
        { await wait; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
}
