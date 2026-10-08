using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

/// <summary>One original phase identity read with immutable downward native grant bounds.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(PartitionMovementAliases.OutcomeQuery)]
internal sealed record PartitionMovementOutcomeQuery(
    [property: global::Orleans.Id(0)] Guid PhaseCommandId,
    [property: global::Orleans.Id(1)] PartitionMovePeerEnvelope Original,
    [property: global::Orleans.Id(2)] long MaximumReadBytes,
    [property: global::Orleans.Id(3)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(4)] int MaximumResultBytes);
