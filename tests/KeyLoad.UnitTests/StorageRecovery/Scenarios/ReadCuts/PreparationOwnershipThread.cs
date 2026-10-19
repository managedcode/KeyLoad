using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Observes foreign-thread refusal while gated, then joins that original worker after the gate unwinds.</summary>
internal sealed class PreparationOwnershipThread
{
    private static readonly TimeSpan ObservationBound = TimeSpan.FromSeconds(10);
    private readonly List<Exception> workerFailures = [];
    private Thread? thread;
    private bool started;

    internal void StartAndObserve(Action probe, string detail)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var ownerThread = Environment.CurrentManagedThreadId;
        var worker = new Thread(() => ServerFailureObserver.Observe(() =>
        {
            if (Environment.CurrentManagedThreadId == ownerThread)
            {
                throw new InvalidOperationException("The execution preparation probe did not leave its owning thread.");
            }
            PreparationOwnershipAssertions.Refuse(probe, detail);
        }, workerFailures))
        {
            IsBackground = false,
            Name = "execution-preparation-foreign-thread"
        };
        thread = worker;
        worker.Start();
        started = true;
        if (!worker.Join(ObservationBound))
        {
            // Throw out of the execution callback first. Its finally must release the gate
            // before JoinAfterCallback waits for this exact original worker.
            throw new TimeoutException("The foreign-thread preparation refusal exceeded its observation bound.");
        }
    }

    internal void JoinAfterCallback(List<Exception> failures)
    {
        var worker = thread;
        if (!started || worker is null)
        {
            return;
        }
        do
        {
            ServerFailureObserver.Observe(worker.Join, failures);
        }
        while (worker.IsAlive);
        failures.AddRange(workerFailures);
    }
}
