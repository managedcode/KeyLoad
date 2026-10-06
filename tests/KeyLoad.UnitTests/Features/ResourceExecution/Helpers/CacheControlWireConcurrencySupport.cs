using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireConcurrencySupport
{
    // Ownership transfers here: the caller must not dispose during a failed finite drain.
    internal static async Task<ConcurrentResult> RunAsync(CacheControlAuthenticator authenticator, CacheReadyProof proof)
    {
        var owner = new CacheControlWireConcurrentTaskOwner();
        var failures = new CacheControlWireConcurrentFailures();
        var barrier = new CacheControlWireConcurrentWorkers.BarrierState();
        try
        {
            owner.ScenarioTask = RunScenarioAsync(authenticator, proof, barrier, owner);
            await ObserveScenarioAsync(owner.ScenarioTask, failures);
        }
        finally
        {
            barrier.Start.TrySetResult();
            owner.FallbackDispatchTask = DispatchFallbackAsync(authenticator, barrier, owner);
            owner.RegisterCleanup(authenticator);
            await DrainAsync(owner, failures);
        }

        failures.ThrowIfAny();
        var results = await owner.WorkerJoin!;
        return new(results.Sum(result => result.Signed), results.Sum(result => result.Closed));
    }

    private static async Task RunScenarioAsync(CacheControlAuthenticator authenticator, CacheReadyProof proof,
        CacheControlWireConcurrentWorkers.BarrierState barrier, CacheControlWireConcurrentTaskOwner owner)
    {
        for (var index = 0; index < owner.Workers.Length; index++)
        {
            owner.Workers[index] = Task.Factory.StartNew(() =>
                CacheControlWireConcurrentWorkers.SignDuringDisposal(authenticator, proof, barrier),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        owner.WorkerJoin = Task.WhenAll(owner.Workers.Select(task => task!));
        owner.DisposeTask = Task.Factory.StartNew(() =>
            CacheControlWireConcurrentWorkers.DisposeDuringWorkers(authenticator, barrier),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await barrier.Ready.Task.WaitAsync(JoinTimeout, TimeProvider.System);
        barrier.Start.TrySetResult();
        await owner.WorkerJoin.WaitAsync(JoinTimeout, TimeProvider.System);
        await owner.DisposeTask.WaitAsync(JoinTimeout, TimeProvider.System);
    }

    private static async Task DispatchFallbackAsync(CacheControlAuthenticator authenticator,
        CacheControlWireConcurrentWorkers.BarrierState barrier, CacheControlWireConcurrentTaskOwner owner)
    {
        // The async original captures dispatch failure so it cannot bypass the common drain.
        owner.FallbackTask = Task.Factory.StartNew(() =>
            CacheControlWireConcurrentWorkers.DisposeAndSignal(authenticator, barrier),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await owner.FallbackTask;
    }

    private static async Task ObserveScenarioAsync(Task scenario, CacheControlWireConcurrentFailures failures)
    {
        var completed = await Task.WhenAny(scenario, Task.Delay(JoinTimeout, TimeProvider.System));
        if (completed != scenario)
        {
            failures.Add(new TimeoutException(ScenarioTimeoutMessage));
        }

        failures.Collect(scenario);
    }

    private static async Task DrainAsync(CacheControlWireConcurrentTaskOwner owner,
        CacheControlWireConcurrentFailures failures)
    {
        var completed = await Task.WhenAny(owner.CleanupObservation!, Task.Delay(JoinTimeout, TimeProvider.System));
        if (completed != owner.CleanupObservation)
        {
            failures.Add(new TimeoutException(OriginalJoinTimeoutMessage));
        }

        foreach (var original in owner.Originals)
        {
            failures.Collect(original, original != owner.WorkerJoin);
        }

        failures.Collect(owner.OriginalJoin!, reportCancellation: false);
        failures.Collect(owner.Cleanup!, reportCancellation: false);
        failures.Collect(owner.CleanupObservation!);
    }

    internal readonly record struct ConcurrentResult(int Signed, int Closed);
    internal const int WorkerCount = 8;
    internal static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(10);
    private const string ScenarioTimeoutMessage = "Concurrent signer scenario exceeded its finite wait bound.";
    private const string OriginalJoinTimeoutMessage = "Original signer operations did not complete within the cleanup bound.";
}
