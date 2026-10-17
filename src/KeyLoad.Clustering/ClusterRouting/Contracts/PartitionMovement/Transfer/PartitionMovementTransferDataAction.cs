namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferDataActionAlias)]
internal enum PartitionMovementTransferDataAction
{
    Open = 1, Page = 2, Close = 3
}
