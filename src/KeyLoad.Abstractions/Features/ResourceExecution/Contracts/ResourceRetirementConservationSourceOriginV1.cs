namespace KeyLoad;

/// <summary>Retains the exact verified source cut when a genuinely fresh offline owner receives its authority records.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementConservationSourceOriginAlias)]
internal sealed record ResourceRetirementConservationSourceOriginV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PhysicalShardRecord SourceOwner,
    [property: Orleans.Id(2)] Guid SourceNodeId,
    [property: Orleans.Id(3)] Guid SourceIncarnation,
    [property: Orleans.Id(4)] long SourcePosition,
    [property: Orleans.Id(5)] long SourceApplied,
    [property: Orleans.Id(6)] ResourceRetirementConservationFloorV1? SourceFloor,
    [property: Orleans.Id(7)] long SourceAuthorityRecordCount,
    [property: Orleans.Id(8)] ReadOnlyMemory<byte> SourceAuthorityDigest,
    [property: Orleans.Id(9)] PhysicalShardRecord TargetOwner);
