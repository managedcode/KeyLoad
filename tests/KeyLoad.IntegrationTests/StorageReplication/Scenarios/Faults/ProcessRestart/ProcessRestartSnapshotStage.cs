namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Closed snapshot points around the original Aspire start operation.</summary>
internal enum ProcessRestartSnapshotStage
{
    BeforeStart,
    AfterStartSucceeded,
    AcceptedStartRetained,
    Failure
}
