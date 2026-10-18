using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeHostFactory
{
    private const int FirstOwner = 0;
    internal static Task<ControlledPartitionMovementNativeHosts> CreateAsync(CancellationToken cancellationToken)
        => CreateAsync(null, null, null, null, null, null, null, cancellationToken);

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithRoutingAsync(
        IOptions<GrainRoutingOptions> routingOptions, Func<int, IGrainRequestPhaseObserver> observerFactory,
        IGrainRequestPurposeObserver? purposeObserver,
        Action<int, IServiceCollection, NodeOptions>? configureServices,
        Action<int, ControlledPartitionMovementNativeHost>? beforeStart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingOptions);
        ArgumentNullException.ThrowIfNull(observerFactory);
        return CreateAsync(null, observerFactory,
            null, routingOptions, purposeObserver, configureServices, beforeStart, cancellationToken);
    }

    internal static async Task<ControlledPartitionMovementNativeHosts> CreateAsync(
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory,
        Func<int, IGrainRequestPhaseObserver>? phaseObserverFactory, int? maximumBatchBytes,
        IOptions<GrainRoutingOptions>? routingOptions, IGrainRequestPurposeObserver? purposeObserver,
        Action<int, IServiceCollection, NodeOptions>? configureServices,
        Action<int, ControlledPartitionMovementNativeHost>? beforeStart, CancellationToken cancellationToken)
    {
        using var endpoints = new ControlledPartitionMovementNativeHostEndpoints();
        var settings = new ControlledPartitionMovementNativeHostSettings(endpoints, maximumBatchBytes, routingOptions);
        var retainedRoot = ControlledPartitionMovementNativeFailureRoot.Capture(settings);
        ControlledPartitionMovementNativeHosts? owner = null;
        var failures = new List<Exception>();
        try
        {
            owner = new ControlledPartitionMovementNativeHosts(settings, new(endpoints.Primary), authenticationObserverFactory, phaseObserverFactory, purposeObserver);
            await owner.CreateNodesAsync(configureServices, cancellationToken).ConfigureAwait(false);
            if (beforeStart is not null)
            {
                for (var index = FirstOwner; index < owner.Nodes.Count; index++)
                {
                    beforeStart(index, owner.Nodes[index]);
                }
            }
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
