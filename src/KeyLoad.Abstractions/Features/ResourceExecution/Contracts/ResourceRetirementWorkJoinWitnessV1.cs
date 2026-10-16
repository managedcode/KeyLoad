namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementWorkJoinWitnessAlias)]
internal sealed record ResourceRetirementWorkJoinWitnessV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] ResourceRetirementOwnerFenceV1 Fence,
    [property: Orleans.Id(2)] PhysicalShardRecord ActualOwner,
    [property: Orleans.Id(3)] string VoterId,
    [property: Orleans.Id(4)] string FullSiloAddress,
    [property: Orleans.Id(5)] Guid StoreNodeId,
    [property: Orleans.Id(6)] long ReadGeneration,
    [property: Orleans.Id(7)] int MatchedCount,
    [property: Orleans.Id(8)] long MaximumPublicationGeneration,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> SnapshotSha256);
