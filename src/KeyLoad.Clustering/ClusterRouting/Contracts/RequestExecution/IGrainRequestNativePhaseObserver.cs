namespace KeyLoad.Clustering;

/// <summary>Receives the same private request phase from actual silo-owned native execution.</summary>
/// <remarks>No activation context, activation identity or producer-disposal notification is manufactured.</remarks>
internal interface IGrainRequestNativePhaseObserver
{
    ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState actualRuntime, CancellationToken cancellationToken);
}
