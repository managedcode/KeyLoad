using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.RetireCancellationQueryAlias)]
internal sealed record PartitionMovementRetireCancellationQuery(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid CancellationCommandId,
    [property: global::Orleans.Id(2)] Guid OriginalPhaseCommandId,
    [property: global::Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: global::Orleans.Id(4)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: global::Orleans.Id(5)] Guid QueryNonce,
    [property: global::Orleans.Id(6)] DateTimeOffset QueryExpiresAt,
    [property: global::Orleans.Id(7)] string CallerVoter,
    [property: global::Orleans.Id(8)] string CallerSiloAddress,
    [property: global::Orleans.Id(9)] long MaximumReadBytes,
    [property: global::Orleans.Id(10)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(11)] int MaximumResultBytes);
