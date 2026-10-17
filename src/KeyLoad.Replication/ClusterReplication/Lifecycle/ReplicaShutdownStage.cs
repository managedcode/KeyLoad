using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

internal static class ReplicaShutdownStage
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;

    internal static async Task RunAsync(ReplicaActivityTracker activity, ReplicaState state,
        CancellationTokenSource lifetime, TaskCompletionSource transportReady, Task? worker,
        ReplicaFollowerSender followers, ReplicaSnapshotReceiver snapshots, ReplicaMaintenance maintenance,
        CancellationToken stoppingToken)
    {
        var drained = activity.Close();
        state.Ready = false;
        var failures = new List<Exception>();
        await ObserveAsync(lifetime.CancelAsync(), failures);
        transportReady.TrySetCanceled(stoppingToken);
        await ObserveAsync(Task.WhenAll(drained, worker ?? Task.CompletedTask), failures);
        await ObserveAsync(Task.WhenAll(followers.DrainAsync(CancellationToken.None),
            snapshots.DrainAsync(CancellationToken.None), maintenance.DrainAsync(CancellationToken.None)), failures);
        ThrowIfAny(failures);
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
    internal static async Task ObserveAsync(Task stage, List<Exception> failures)
    {
        // A fault never skips the remaining physical owners' terminal drains.
        await stage.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        if (stage.Exception is { } failure)
        {
            failures.AddRange(failure.InnerExceptions);
        }
        else if (stage.IsCanceled)
        {
            failures.Add(new TaskCanceledException(stage));
        }
    }

    internal static async Task DisposeAsync(Task originalShutdown, ReplicaLeader leader,
        ReplicaSnapshotReceiver snapshots, ReplicaFollowerSender followers, CancellationTokenSource lifetime)
    {
        var failures = new List<Exception>();
        await ReplicaShutdownStage.ObserveAsync(originalShutdown, failures).ConfigureAwait(false);
        try
        {
            leader.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            snapshots.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            followers.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        try
        {
            lifetime.Dispose();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        ReplicaShutdownStage.ThrowIfAny(failures);
    }
}
