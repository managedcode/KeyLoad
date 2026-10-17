namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Closed failure points recorded for an unchanged native restart attempt.</summary>
internal enum ContainerRestartStage
{
    StartCommand,
    HealthWait,
    RuntimeInspection,
    IdentityValidation,
    SourceReceipt
}
