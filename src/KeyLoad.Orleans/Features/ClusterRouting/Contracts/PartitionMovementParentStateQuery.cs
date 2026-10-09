namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.ParentStateQueryAlias)]
internal sealed record PartitionMovementParentStateQuery(
    [property: global::Orleans.Id(0)] PartitionMoveRequest Request,
    [property: global::Orleans.Id(1)] Guid SelectedOriginalPhaseCommandId,
    [property: global::Orleans.Id(2)] long MaximumReadBytes,
    [property: global::Orleans.Id(3)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(4)] int MaximumResultBytes);
