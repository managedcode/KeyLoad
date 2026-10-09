using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ReceiverIssueQueryAlias)]
internal sealed record PartitionMovementReceiverIssueQuery(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: global::Orleans.Id(2)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: global::Orleans.Id(3)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: global::Orleans.Id(4)] Guid QueryNonce,
    [property: global::Orleans.Id(5)] DateTimeOffset QueryExpiresAt,
    [property: global::Orleans.Id(6)] string CallerVoter,
    [property: global::Orleans.Id(7)] string CallerSiloAddress,
    [property: global::Orleans.Id(8)] long MaximumReadBytes,
    [property: global::Orleans.Id(9)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(10)] int MaximumResultBytes);
