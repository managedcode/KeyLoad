using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Binds a closed writer origin and the exact native membership family to its original source fence.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ResourceOwnerInventoryMovementProtocol.ProofAlias)]
internal sealed record ResourceOwnerInventoryMovementProofV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(2)] ResourceOwnerInventoryOriginV1? Origin,
    [property: Orleans.Id(3)] long MemberCount,
    [property: Orleans.Id(4)] string MemberDigest);
