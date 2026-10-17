using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ReceiverIssueRequestAlias)]
internal sealed record PartitionMovementReceiverIssueRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid IssuanceCommandId,
    [property: global::Orleans.Id(2)] Guid OriginalPhaseCommandId,
    [property: global::Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: global::Orleans.Id(4)] PartitionMoveReceiverSourceWitness SourceWitness,
    [property: global::Orleans.Id(5)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: global::Orleans.Id(6)] string CallerVoter,
    [property: global::Orleans.Id(7)] string CallerSiloAddress,
    [property: global::Orleans.Id(8)] Guid IssuanceNonce,
    [property: global::Orleans.Id(9)] PartitionMoveJournalReceipt SourceProofCheckpointReceipt);
