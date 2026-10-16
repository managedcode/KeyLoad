using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeHostFactory
{
    internal static Task<ControlledPartitionMovementNativeHosts> CreateAsync(
        Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory, CancellationToken cancellationToken)
        => CreateAsync(observerFactory, null, cancellationToken);

    internal static async Task<ControlledPartitionMovementNativeHosts> CreateAsync(
        Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory, CancellationToken cancellationToken,
        int? maximumBatchBytes = null)
    {
        using var endpoints = new ControlledPartitionMovementNativeHostEndpoints();
        var settings = new ControlledPartitionMovementNativeHostSettings(endpoints, maximumBatchBytes);
        var retainedRoot = ControlledPartitionMovementNativeFailureRoot.Capture(settings);
        ControlledPartitionMovementNativeHosts? owner = null;
        var failures = new List<Exception>();
        try
        {
            owner = new ControlledPartitionMovementNativeHosts(settings, new(endpoints.Primary), observerFactory, authenticationObserverFactory);
            await owner.CreateNodesAsync(cancellationToken).ConfigureAwait(false);
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
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
