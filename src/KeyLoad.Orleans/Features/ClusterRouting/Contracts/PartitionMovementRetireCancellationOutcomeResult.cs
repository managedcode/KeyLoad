using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.RetireCancellationOutcomeResultAlias)]
internal sealed record PartitionMovementRetireCancellationOutcomeResult(
    [property: global::Orleans.Id(0)] PartitionMoveRetireCancellationSnapshot Snapshot,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
