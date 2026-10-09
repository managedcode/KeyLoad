namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferDataActionAlias)]
internal enum PartitionMovementTransferDataAction { Open = 1, Page = 2, Close = 3 }

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.TransferDataCapabilityAlias)]
internal sealed record PartitionMovementTransferDataCapability(
    [property: global::Orleans.Id(0)] PartitionMovementTransferDataAction Action,
    [property: global::Orleans.Id(1)] ReadOnlyMemory<byte> OriginalAuthorityReplyBytes,
    [property: global::Orleans.Id(2)] string OriginalAuthoritySignature,
    [property: global::Orleans.Id(3)] Guid HandleId,
    [property: global::Orleans.Id(4)] int Ordinal,
    [property: global::Orleans.Id(5)] long MaximumReadBytes,
    [property: global::Orleans.Id(6)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(7)] int MaximumResultBytes);
