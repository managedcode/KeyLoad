using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.RetireCancellationRequestAlias)]
internal sealed record PartitionMovementRetireCancellationRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid CancellationCommandId,
    [property: global::Orleans.Id(2)] Guid OriginalPhaseCommandId,
    [property: global::Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: global::Orleans.Id(4)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: global::Orleans.Id(5)] long CleanupGeneration,
    [property: global::Orleans.Id(6)] PartitionMoveReceiverSourceWitness SourceWitness,
    [property: global::Orleans.Id(7)] Guid CancellationNonce,
    [property: global::Orleans.Id(8)] DateTimeOffset CancellationExpiresAt,
    [property: global::Orleans.Id(9)] string CallerVoter,
    [property: global::Orleans.Id(10)] string CallerSiloAddress);
