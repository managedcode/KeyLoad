namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferDataResultAlias)]
internal sealed record PartitionMovementTransferDataResult(
    [property: global::Orleans.Id(0)] PartitionMovementTransferDataAction Action,
    [property: global::Orleans.Id(1)] PartitionMovementTransferHandle? Handle,
    [property: global::Orleans.Id(2)] PartitionMovementPageResult? Page,
    [property: global::Orleans.Id(3)] PartitionMovementTransferClosed? Closed,
    [property: global::Orleans.Id(4)] long ReadBytes,
    [property: global::Orleans.Id(5)] int ExaminedRecords);
