namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Separates bounded general failures from the sampled restart attempt receipts.</summary>
internal enum ClusterFailureReceiptKind
{
    General,
    Restart
}
