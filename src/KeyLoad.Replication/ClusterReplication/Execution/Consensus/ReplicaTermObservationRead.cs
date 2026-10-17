namespace KeyLoad.Replication;

internal readonly struct ReplicaTermObservationRead(long term, ReplicaTermObservation observation)
{
    internal long Term { get; } = term;
    internal ReplicaTermObservation Observation { get; } = observation;
}
