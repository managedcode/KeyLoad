namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferClosedAlias)]
internal sealed record PartitionMovementTransferClosed(
    [property: global::Orleans.Id(0)] Guid HandleId,
    [property: global::Orleans.Id(1)] PartitionMovementTransferCloseDisposition Disposition);
