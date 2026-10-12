namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>A fresh native caller proof around immutable original movement bytes.</summary>
[GenerateSerializer, Alias(PartitionMovementGrainServiceProtocol.RequestAlias)]
internal sealed record PartitionMovementGrainServiceRequest(
    [property: Id(PartitionMovementGrainServiceProtocol.RequestChannel)] PartitionMovementGrainServiceChannel Channel,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestQuery)] bool Query,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestPayload)] ReadOnlyMemory<byte> Payload,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestSignature)] string Signature,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestCallerVoter)] string CallerVoter,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestCallerSiloAddress)] string CallerSiloAddress,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestDestinationVoter)] string DestinationVoter,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestDestinationSiloAddress)] string DestinationSiloAddress,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestNonce)] Guid Nonce,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestExpiresAt)] DateTimeOffset ExpiresAt,
    [property: Id(PartitionMovementGrainServiceProtocol.RequestSenderSignature)] string SenderSignature);
