namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementConservationStepAlias)]
internal sealed record ResourceRetirementConservationStepV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(2)] long Revision,
    [property: Orleans.Id(3)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(4)] Guid RetirementId,
    [property: Orleans.Id(5)] Guid ReleaseCommandId,
    [property: Orleans.Id(6)] long AppliedPosition,
    [property: Orleans.Id(7)] ReadOnlyMemory<byte> PreviousDigest,
    [property: Orleans.Id(8)] ReadOnlyMemory<byte> Digest)
{
    [Orleans.Id(9)]
    public long AddedAuthorityRecords
    {
        get; init;
    }
    [Orleans.Id(10)]
    public ReadOnlyMemory<byte> AddedAuthorityDigest
    {
        get; init;
    }
}
