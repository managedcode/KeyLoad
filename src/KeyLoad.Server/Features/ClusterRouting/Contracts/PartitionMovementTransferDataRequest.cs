using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.TransferDataRequestAlias)]
internal sealed record PartitionMovementTransferDataRequest(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] Guid Nonce,
    [property: global::Orleans.Id(2)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(3)] string OperatorPrincipalId,
    [property: global::Orleans.Id(4)] string CallerVoter,
    [property: global::Orleans.Id(5)] string CallerSiloAddress,
    [property: global::Orleans.Id(6)] PartitionMovementTransferDataCapability Capability);
