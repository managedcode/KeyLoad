namespace KeyLoad.Orleans;

internal interface IGrainPartitionMovementJournalObserver
{
    void CaptureReturned(GrainRequestProbeIdentity identity, OperationResult actual,
        global::Orleans.Runtime.IGrainContext context);
}
