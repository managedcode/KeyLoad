using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.ParentStateResultAlias)]
internal sealed record PartitionMovementParentStateResult(
    [property: global::Orleans.Id(0)] PartitionMoveParentState State,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
