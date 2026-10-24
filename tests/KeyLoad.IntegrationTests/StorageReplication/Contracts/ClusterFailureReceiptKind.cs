namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Separates bounded general failures from the captured restart attempt receipts.</summary>
internal enum ClusterFailureReceiptKind
{
    General,
    Restart
}
