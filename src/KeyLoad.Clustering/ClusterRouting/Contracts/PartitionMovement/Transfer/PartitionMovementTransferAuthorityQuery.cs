namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferAuthorityQueryAlias)]
internal sealed record PartitionMovementTransferAuthorityQuery(
    [property: global::Orleans.Id(0)] PartitionMoveRequest Request,
    [property: global::Orleans.Id(1)] Guid CapturePhaseCommandId,
    [property: global::Orleans.Id(2)] long MaximumReadBytes,
    [property: global::Orleans.Id(3)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(4)] int MaximumResultBytes);
