namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferCloseDispositionAlias)]
internal enum PartitionMovementTransferCloseDisposition
{
    Joined = 1, AlreadyAbsent = 2
}
