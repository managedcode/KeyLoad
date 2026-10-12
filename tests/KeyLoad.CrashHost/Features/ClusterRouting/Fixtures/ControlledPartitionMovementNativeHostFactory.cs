using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeHostFactory
{
    internal static async Task<ControlledPartitionMovementNativeHosts> CreateAsync(
        Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory, CancellationToken cancellationToken)
    {
        using var endpoints = new ControlledPartitionMovementNativeHostEndpoints();
        var settings = new ControlledPartitionMovementNativeHostSettings(endpoints);
        var retainedRoot = ControlledPartitionMovementNativeFailureRoot.Capture(settings);
        ControlledPartitionMovementNativeHosts? owner = null;
        var failures = new List<Exception>();
        try
        {
            owner = new ControlledPartitionMovementNativeHosts(settings, new(endpoints.Primary), observerFactory);
            await owner.CreateNodesAsync().ConfigureAwait(false);
            endpoints.Dispose();
            await ControlledPartitionMovementNativeHosts.JoinOriginalAsync(
                Task.WhenAll(owner.Nodes.Select(node => node.StartAsync(cancellationToken)))).ConfigureAwait(false);
            var transferred = owner;
            owner = null;
            return transferred;
        }
        catch (Exception primary)
        {
            owner?.RetainCreationFailure(primary);
            failures.Add(primary);
            ServerFailureObserver.Observe(() => retainedRoot.Attach(primary), failures);
            throw;
        }
        finally
        {
            try
            {
                var cleanup = owner?.DisposeAsync();
                if (cleanup is { } original)
                {
                    var originalTask = original.AsTask();
                    await ServerFailureObserver.ObserveAsync(() => originalTask, failures).ConfigureAwait(false);
                }
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
