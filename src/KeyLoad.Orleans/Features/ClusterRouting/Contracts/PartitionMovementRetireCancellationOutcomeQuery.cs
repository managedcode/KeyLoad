using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.RetireCancellationOutcomeQueryAlias)]
internal sealed record PartitionMovementRetireCancellationOutcomeQuery(
    [property: global::Orleans.Id(0)] PartitionMoveRetireCancellationReadBody Body,
    [property: global::Orleans.Id(1)] long MaximumReadBytes,
    [property: global::Orleans.Id(2)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(3)] int MaximumResultBytes);
