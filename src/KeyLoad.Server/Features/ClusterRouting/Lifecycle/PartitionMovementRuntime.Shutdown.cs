using KeyLoad.Orleans;
namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementRuntime
{
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
            shutdown ??= CloseAsync(active.Values.Select(value => value.Task).ToArray());
            return new(shutdown);
        }
    }

    private async Task CloseAsync(Task[] originalOperations)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(stopping.CancelAsync, failures).ConfigureAwait(false);
        foreach (var original in originalOperations)
        { await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false); }
        if (source is not null)
        { await ServerFailureObserver.ObserveAsync(() => source.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (client is not null)
        { ServerFailureObserver.Observe(client.Dispose, failures); }
        if (admission is not null)
        { ServerFailureObserver.Observe(admission.Dispose, failures); }
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        lock (gate)
        { joined = true; }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
