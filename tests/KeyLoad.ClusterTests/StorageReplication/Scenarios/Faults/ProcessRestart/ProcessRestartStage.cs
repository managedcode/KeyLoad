namespace KeyLoad.ClusterTests.StorageReplication;

/// <summary>Closed failure points recorded for an unchanged execution restart attempt.</summary>
internal enum ProcessRestartStage
{
    StartCommand,
    HealthWait,
    RuntimeInspection,
    IdentityValidation,
    SourceReceipt
}
