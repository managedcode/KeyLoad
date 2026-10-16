using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeObserverShutdown
{
    internal static ValueTask DisposeAsync(ref Task? shutdown, Lock disposeSync,
        RequestCqrsProbeLifecycle lifecycle, CancellationTokenSource stopping,
        Lazy<ConnectionProbeCapture> connections)
    {
        TaskCompletionSource start;
        Task completion;
        lock (disposeSync)
        {
            if (shutdown is not null)
            {
                return new ValueTask(shutdown);
            }
            // OrleansNode drains admitted work before silo stop; this joins callbacks before disposing observer state.
            var join = lifecycle.StopAdmissionAndJoin();
            start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdown = JoinAsync(start.Task, join, stopping, connections);
            completion = shutdown;
        }
        start.SetResult();
        return new ValueTask(completion);
    }

    internal static async Task JoinAsync(Task start, Task join, CancellationTokenSource stopping,
        Lazy<ConnectionProbeCapture> connections)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(stopping.Cancel, failures);
        await ServerFailureObserver.ObserveAsync(() => join, failures).ConfigureAwait(false);
        if (connections.IsValueCreated)
        {
            await ServerFailureObserver.ObserveAsync(() => connections.Value.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
