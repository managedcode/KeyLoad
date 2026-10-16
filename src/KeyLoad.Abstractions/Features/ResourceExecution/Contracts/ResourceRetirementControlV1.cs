using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementControlAlias)]
internal sealed record ResourceRetirementControlV1(
    [property: Orleans.Id(0)] ResourceRetirementRequestV1 Request,
    [property: Orleans.Id(1)] ResourceDefinition Definition,
    [property: Orleans.Id(2)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(3)] PhysicalShardCatalog Catalog,
    [property: Orleans.Id(4)] PhysicalOwnerDirectoryV1 Directory,
    [property: Orleans.Id(5)] long PlacementDirectoryRevision,
    [property: Orleans.Id(6)] string PrincipalId,
    [property: Orleans.Id(7)] long PolicyEpoch,
    [property: Orleans.Id(8)] ResourceRetirementPhaseReceiptV1 BeginReceipt,
    [property: Orleans.Id(9)] string IntentDigest)
{
    [Orleans.Id(10)]
    public ImmutableArray<ResourceRetirementWorkOriginV1> WorkOrigins
    {
        get; init;
    }
}
