namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal enum LiveRangeCancellationPhase
{
    Before,
    BaselineObserver,
    StagedObserver,
    VisitorStop,
    VisitorContinue
}
