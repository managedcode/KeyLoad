using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed partial class PartitionMovementSourceOwner
{
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            shutdown ??= CloseAsync(captures.Values.Select(value => value.Completion.Task).ToArray());
            return new(shutdown);
        }
    }

    private async Task CloseAsync(Task[] capturing)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(stopping.CancelAsync, failures).ConfigureAwait(false);
        foreach (var original in capturing)
        { await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false); }
        PartitionMovementSourceEntry[] retained;
        lock (gate)
        { retained = sessions.Values.Concat(closedMoves.Values.SelectMany(value => value)).Distinct().ToArray(); }
        foreach (var entry in retained)
        {
            await ServerFailureObserver.ObserveAsync(() => entry.Session.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(entry.ReleaseWork, failures);
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
        { ServerFailureObserver.Observe(original.Dispose, failures); }
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
