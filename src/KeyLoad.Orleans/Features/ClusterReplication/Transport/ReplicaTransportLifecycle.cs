using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Drains node maintenance after membership shutdown, while Orleans transport still exists.</summary>
/// <param name="endpoint">The node-owned replica endpoint to stop after discovery is withdrawn.</param>
/// <param name="discovery">The local runtime state to mark unavailable before draining.</param>
/// <param name="nativeDiscovery">Native metadata callback owner joined before endpoint shutdown.</param>
public sealed class ReplicaTransportLifecycle(IReplicaEndpoint endpoint, ReplicaSiloDiscoveryState discovery,
    ReplicaNativeDiscoveryRuntime nativeDiscovery)
    : ILifecycleParticipant<ISiloLifecycle>
{
    private const int MaximumShutdownStages = 2;

    /// <summary>Registers transport shutdown at RuntimeStorageServices, after membership teardown.</summary>
    /// <param name="lifecycle">The Orleans silo lifecycle to subscribe to.</param>
    public void Participate(ISiloLifecycle lifecycle) => lifecycle.Subscribe(ReplicaTransportProtocol.LifecycleName,
        ServiceLifecycleStage.RuntimeStorageServices, _ => Task.CompletedTask, StopAsync);

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        discovery.StopDiscovery();
        var failures = new List<Exception>(MaximumShutdownStages);
        await ObserveAsync(nativeDiscovery.StopAsync, failures).ConfigureAwait(false);
        await ObserveAsync(() => endpoint.StopAsync(cancellationToken), failures).ConfigureAwait(false);
        ReplicaDiscoveryLifetime.ThrowIfAny(failures);
    }
    private static async Task ObserveAsync(Func<Task> stop, List<Exception> failures)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }
}
