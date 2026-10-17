using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.RequestAlias)]
internal sealed record PartitionMovementTransportRequest(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] PartitionMovePeerEnvelope Envelope,
    [property: global::Orleans.Id(2)] PartitionMoveJournalReceipt? ControlGrantJournal,
    [property: global::Orleans.Id(3)] string CallerVoter,
    [property: global::Orleans.Id(4)] string CallerSiloAddress,
    [property: global::Orleans.Id(5)] PartitionMovementTransportAction Action,
    [property: global::Orleans.Id(6)] Guid HandleId,
    [property: global::Orleans.Id(7)] int Ordinal);
