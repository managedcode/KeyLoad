using KeyLoad.Orleans;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRequestWorkKernelCleanup
{
    private const int SignalJoinSeconds = 10;
    internal static async Task CleanupAsync(NativeRequestWorkOwner owner, NativeRequestWorkProbe probe,
        IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> enumerator,
        Task<bool>? firstMove, Task? disposal, Task? drain, HashSet<Task> observed,
        IReadOnlyList<IDisposable>? additionalLeases = null)
    {
        var failures = new List<Exception>();
        probe.ReleaseProducer();
        if (additionalLeases is not null)
        {
            foreach (var lease in additionalLeases)
            {
                ServerFailureObserver.Observe(lease.Dispose, failures);
            }
        }
        await ObserveIfNeededAsync(firstMove, observed, failures);
        disposal ??= enumerator.DisposeAsync().AsTask();
        await ObserveIfNeededAsync(disposal, observed, failures);
        drain ??= owner.DrainAsync();
        await ObserveIfNeededAsync(drain, observed, failures);
        if (probe.HandlerEntered.IsCompleted)
        {
            await ServerFailureObserver.ObserveAsync(
                () => probe.ProducerSettled.WaitAsync(TimeSpan.FromSeconds(SignalJoinSeconds), TimeProvider.System), failures);
        }
        if (firstMove is not null)
        {
            await ServerFailureObserver.ObserveAsync(
                () => probe.ActivationSettled.WaitAsync(TimeSpan.FromSeconds(SignalJoinSeconds), TimeProvider.System), failures);
        }
        await ObserveIfNeededAsync(owner.DisposeAsync().AsTask(), observed, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ObserveBoundedAsync(Task operation, List<Exception> failures,
        HashSet<Task> observed, TimeSpan bound)
    {
        await ServerFailureObserver.ObserveAsync(() => operation.WaitAsync(bound, TimeProvider.System), failures);
        if (operation.IsCompleted)
        {
            observed.Add(operation);
        }
    }

    internal static async Task AssertContainsFailureAsync(IReadOnlyList<Exception> failures, Exception expected)
    {
        var leaves = new List<Exception>();
        foreach (var failure in failures)
        {
            AddLeaves(failure, leaves);
        }

        await Assert.That(leaves.Any(failure => ReferenceEquals(failure, expected))).IsTrue();
    }

    private static async Task ObserveIfNeededAsync(Task? task, HashSet<Task> observed, List<Exception> failures)
    {
        if (task is null || observed.Contains(task))
        {
            return;
        }

        await ServerFailureObserver.ObserveAsync(() => task, failures);
        observed.Add(task);
    }

    private static void AddLeaves(Exception error, List<Exception> leaves)
    {
        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                AddLeaves(inner, leaves);
            }
            return;
        }

        leaves.Add(error);
    }
}
