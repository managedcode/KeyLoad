namespace KeyLoad.FunctionalTests.StorageRecovery;

internal enum LiveRangeCancellationPhase
{
    Before,
    BaselineObserver,
    StagedObserver,
    VisitorStop,
    VisitorContinue
}
