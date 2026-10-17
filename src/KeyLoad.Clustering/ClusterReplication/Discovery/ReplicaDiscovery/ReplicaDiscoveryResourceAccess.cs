namespace KeyLoad.Clustering;

/// <summary>Provides bounded operations which borrow the discovery resource owner's exchange and gate.</summary>
internal sealed class ReplicaDiscoveryResourceAccess(ReplicaDiscoveryResources owner)
{
    internal Task<ReplicaDiscoveryObservation?> DiscoverAsync(string voterId, CancellationToken cancellationToken)
        => owner.DiscoverAsync(voterId, cancellationToken);

    internal Task WaitForDiscoveryAsync(CancellationToken cancellationToken)
        => owner.WaitForDiscoveryAsync(cancellationToken);

    internal void ReleaseDiscoveryGate() => owner.ReleaseDiscoveryGate();
}
