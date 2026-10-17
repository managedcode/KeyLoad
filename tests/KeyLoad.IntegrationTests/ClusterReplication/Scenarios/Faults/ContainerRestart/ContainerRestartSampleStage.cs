namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Closed sample points around the original Aspire start operation.</summary>
internal enum ContainerRestartSampleStage
{
    BeforeStart,
    AfterStartSucceeded,
    AcceptedStartRetained,
    Failure
}
