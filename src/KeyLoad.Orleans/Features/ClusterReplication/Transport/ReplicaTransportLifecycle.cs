using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Drains node maintenance after membership shutdown, while Orleans transport still exists.</summary>
/// <param name="endpoint">The node-owned replica endpoint to stop after discovery is withdrawn.</param>
/// <param name="discovery">The local runtime state to mark unavailable before draining.</param>
public sealed class ReplicaTransportLifecycle(IReplicaEndpoint endpoint, ReplicaSiloDiscoveryState discovery)
    : ILifecycleParticipant<ISiloLifecycle>
{
    /// <summary>Registers transport shutdown at RuntimeStorageServices, after membership teardown.</summary>
    /// <param name="lifecycle">The Orleans silo lifecycle to subscribe to.</param>
    public void Participate(ISiloLifecycle lifecycle) => lifecycle.Subscribe(ReplicaTransportProtocol.LifecycleName,
        ServiceLifecycleStage.RuntimeStorageServices, _ => Task.CompletedTask, StopAsync);

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        discovery.StopDiscovery();
        await endpoint.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
