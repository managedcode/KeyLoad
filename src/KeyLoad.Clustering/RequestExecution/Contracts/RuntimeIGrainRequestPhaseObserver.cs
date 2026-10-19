namespace KeyLoad.Clustering;

/// <summary>Receives the same private request phase from actual silo-owned execution execution.</summary>
/// <remarks>No activation context, activation identity or producer-disposal notification is manufactured.</remarks>
internal interface RuntimeIGrainRequestPhaseObserver
{
    ValueTask RuntimeObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken cancellationToken);
}
