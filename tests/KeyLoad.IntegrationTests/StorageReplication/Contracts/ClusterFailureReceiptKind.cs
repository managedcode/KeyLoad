namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Separates bounded general failures from the captured restart attempt receipts.</summary>
internal enum ClusterFailureReceiptKind
{
    General,
    Restart
}
