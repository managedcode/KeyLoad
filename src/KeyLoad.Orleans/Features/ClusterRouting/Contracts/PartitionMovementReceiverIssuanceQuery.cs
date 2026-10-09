using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementContractNames.ReceiverIssuanceQueryAlias)]
internal sealed record PartitionMovementReceiverIssuanceQuery(
    [property: global::Orleans.Id(0)] Guid OriginalPhaseCommandId,
    [property: global::Orleans.Id(1)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: global::Orleans.Id(2)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: global::Orleans.Id(3)] long MaximumReadBytes,
    [property: global::Orleans.Id(4)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(5)] int MaximumResultBytes);
