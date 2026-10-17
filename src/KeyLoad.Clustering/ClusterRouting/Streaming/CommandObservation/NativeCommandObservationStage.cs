namespace KeyLoad.Clustering;

internal enum NativeCommandObservationStage
{
    Reserved,
    Prepared,
    CommandExecuting,
    SubmitSettled,
    ReadExecuting,
    Settling,
    Settled
}
