namespace KeyLoad.Orleans;

/// <summary>Receives a validated protocol-incompatible native peer observation for private diagnostics.</summary>
internal interface IReplicaDiscoveryObservationSink
{
    ValueTask ObserveIncompatibleAsync(string voterId, ReplicaDiscoveryObservation observation,
        CancellationToken cancellationToken);
}
