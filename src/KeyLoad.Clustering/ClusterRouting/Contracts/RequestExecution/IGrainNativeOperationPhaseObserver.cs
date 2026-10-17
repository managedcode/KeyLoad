namespace KeyLoad.Clustering;

/// <summary>Optional private observer of an independently owned, already-issued verified native operation.</summary>
/// <remarks>Does not issue or modify authority and never receives a borrowed grain context.</remarks>
internal interface IGrainNativeOperationPhaseObserver
{
    void ObserveNativeOperation(GrainRequestProbeIdentity identity, ReplicatedOperation original);
}
