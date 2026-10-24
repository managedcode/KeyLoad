namespace KeyLoad.UnitTests.StorageRecovery;

internal enum LiveRangeCancellationPhase
{
    Before,
    BaselineObserver,
    StagedObserver,
    VisitorStop,
    VisitorContinue
}
