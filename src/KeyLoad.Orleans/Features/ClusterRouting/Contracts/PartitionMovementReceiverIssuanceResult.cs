using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.ReceiverIssuanceResultAlias)]
internal sealed record PartitionMovementReceiverIssuanceResult(
    [property: global::Orleans.Id(0)] PartitionMoveReceiverIssuanceSnapshot Snapshot,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
