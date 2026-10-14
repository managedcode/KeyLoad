using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class PartitionMovementSourceShutdown
{
    internal static async Task JoinAsync(Task[] capturing, CancellationTokenSource stopping, Lock gate,
        Dictionary<Guid, IPartitionMovementRetainedSourceImage> sessions,
        Dictionary<PartitionMovementSourceScope, IPartitionMovementRetainedSourceImage[]> closedMoves,
        Dictionary<PartitionMovementSourceScope, NativeRequestWorkLease> closedAdmissions)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(stopping.CancelAsync, failures).ConfigureAwait(false);
        foreach (var original in capturing)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        IPartitionMovementRetainedSourceImage[] retained;
        lock (gate)
        {
            retained = sessions.Values.Concat(closedMoves.Values.SelectMany(value => value)).Distinct().ToArray();
        }
        foreach (var entry in retained)
        {
            await ServerFailureObserver.ObserveAsync(() => entry.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        NativeRequestWorkLease[] closureWork;
        lock (gate)
        {
            closureWork = closedAdmissions.Values.ToArray();
            sessions.Clear();
            closedMoves.Clear();
            closedAdmissions.Clear();
        }
        foreach (var original in closureWork)
        {
            ServerFailureObserver.Observe(original.Dispose, failures);
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
