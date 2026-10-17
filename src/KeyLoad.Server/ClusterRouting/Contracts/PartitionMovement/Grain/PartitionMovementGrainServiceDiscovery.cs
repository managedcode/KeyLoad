namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Actual native silo identity authenticated by the unchanged discovery proof.</summary>
[GenerateSerializer, Alias(PartitionMovementGrainServiceProtocol.DiscoveryAlias)]
internal sealed record PartitionMovementGrainServiceDiscovery(
    [property: Id(PartitionMovementGrainServiceProtocol.DiscoveryPayload)] ReadOnlyMemory<byte> Payload,
    [property: Id(PartitionMovementGrainServiceProtocol.DiscoverySignature)] string Signature);
