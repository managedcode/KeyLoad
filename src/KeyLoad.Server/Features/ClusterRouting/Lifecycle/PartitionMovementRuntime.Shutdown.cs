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
        {
            Task? sourceShutdown = null;
            try
            { sourceShutdown = source.DisposeAsync().AsTask(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            if (sourceShutdown is not null)
            { await ServerFailureObserver.ObserveAsync(() => sourceShutdown, failures).ConfigureAwait(false); }
        }
        if (client is not null)
        {
            try
            { client.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        if (admission is not null)
        {
            try
            { admission.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        parent = null;
        parentDatabase = null;
        parentClock = null;
        lock (gate)
        { joined = true; }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
