using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

internal static class ReplicaMaterializerShutdown
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;

    internal static async Task DisposeAsync(CancellationTokenSource lifetime, ChannelWriter<bool> work, Task worker,
        SemaphoreSlim applyGate, Action publishTerminal)
    {
        var cancellation = lifetime.CancelAsync();
        work.TryComplete();
        List<Exception> failures = [];
        await ReplicaShutdownStage.ObserveAsync(Task.WhenAll(cancellation, worker), failures).ConfigureAwait(false);
        await DrainAsync(applyGate, publishTerminal, failures).ConfigureAwait(false);
        DisposeLifetime(lifetime, failures);
        if (failures.Count == SingleFailure)
        {
            ExceptionDispatchInfo.Capture(failures[FirstFailure]).Throw();
        }
        if (failures.Count > SingleFailure)
        {
            throw new AggregateException(failures);
        }
    }

    private static async Task DrainAsync(SemaphoreSlim applyGate, Action publishTerminal, List<Exception> failures)
    {
        try
        {
            publishTerminal();
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
            await applyGate.WaitAsync().ConfigureAwait(false);
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
            applyGate.Dispose();
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

    private static void DisposeLifetime(CancellationTokenSource lifetime, List<Exception> failures)
    {
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
    }
}
