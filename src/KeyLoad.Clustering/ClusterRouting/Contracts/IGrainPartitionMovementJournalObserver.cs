namespace KeyLoad.Clustering;

internal interface IGrainPartitionMovementJournalObserver
{
    void CaptureSealed(GrainRequestProbeIdentity identity, ReplicatedOperation original,
        global::Orleans.Runtime.IGrainContext context);

    void CaptureReturned(GrainRequestProbeIdentity identity, OperationResult actual,
        global::Orleans.Runtime.IGrainContext context);
}
