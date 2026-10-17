using System.Runtime.ExceptionServices;
using KeyLoad.Core;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Joins the original node admission worker before releasing its owned primitives.</summary>
internal static class ReplicaCoordinatorShutdown
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;

    internal static async Task DrainAsync(AdmittedCommandInbox commands, CancellationTokenSource lifetime, Task worker)
    {
        var failures = new List<Exception>();
        Observe(commands.Stop, failures);
        var cancellation = Task.CompletedTask;
        Observe(() => cancellation = lifetime.CancelAsync(), failures);
        await ReplicaShutdownStage.ObserveAsync(Task.WhenAll(cancellation, worker), failures).ConfigureAwait(false);
        ThrowIfAny(failures);
    }

    internal static async Task<List<Exception>> JoinDisposalAsync(Task originalShutdown)
    {
        var failures = new List<Exception>();
        await ReplicaShutdownStage.ObserveAsync(originalShutdown, failures).ConfigureAwait(false);
        return failures;
    }

    private static void Observe(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
    }

    internal static void ThrowIfAny(List<Exception> failures)
    {
        if (failures.Count == SingleFailure)
        {
            ExceptionDispatchInfo.Capture(failures[FirstFailure]).Throw();
        }
        if (failures.Count > SingleFailure)
        {
            throw new AggregateException(failures);
        }
    }

    internal static async Task DisposeAsync(Task originalShutdown, AdmittedCommandInbox commands,
        CancellationTokenSource lifetime)
    {
        var failures = await ReplicaCoordinatorShutdown.JoinDisposalAsync(originalShutdown).ConfigureAwait(false);
        try
        {
            await ReplicaShutdownStage.ObserveAsync(commands.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            DisposeLifetime(lifetime, failures);
            throw;
        }
        DisposeLifetime(lifetime, failures);
    }

    private static void DisposeLifetime(CancellationTokenSource lifetime, List<Exception> failures)
    {
        try
        {
            lifetime.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            ReplicaCoordinatorShutdown.ThrowIfAny(failures);
            throw;
        }
        ReplicaCoordinatorShutdown.ThrowIfAny(failures);
    }
}
