namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementConservationFloorAlias)]
internal sealed record ResourceRetirementConservationFloorV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(2)] long Revision,
    [property: Orleans.Id(3)] long RetiredLifetimeCount,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> Digest)
{
    [Orleans.Id(5)]
    public long AuthorityRecordCount
    {
        get; init;
    }
    [Orleans.Id(6)]
    public ReadOnlyMemory<byte> AuthorityDigest
    {
        get; init;
    }
    [Orleans.Id(7)]
    public ReadOnlyMemory<byte> BaselineOriginKey
    {
        get; init;
    }
}
