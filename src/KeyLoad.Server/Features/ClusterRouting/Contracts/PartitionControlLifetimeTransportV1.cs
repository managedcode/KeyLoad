using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Transports exact original request bytes and their source-admitted resource lifetime proof.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionControlLifetimeTransportProtocol.Alias)]
internal sealed record PartitionControlLifetimeTransportV1(
    [property: Orleans.Id(PartitionControlLifetimeTransportProtocol.OriginalRequestId)] ReadOnlyMemory<byte> OriginalRequest,
    [property: Orleans.Id(PartitionControlLifetimeTransportProtocol.ProofId)] PartitionMoveLifetimeProofV1 Proof);
