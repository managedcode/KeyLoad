namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Separates bounded general failures from the captured restart attempt receipts.</summary>
internal enum ClusterFailureReceiptKind
{
    General,
    Restart
}
