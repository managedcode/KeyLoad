using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparationControlAlias)]
internal sealed record ResourceRetirementPreparationControlV1(
    [property: Orleans.Id(0)] ResourceRetirementRequestV1 Request,
    [property: Orleans.Id(1)] ResourceDefinition Definition,
    [property: Orleans.Id(2)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(3)] PhysicalShardCatalog Catalog,
    [property: Orleans.Id(4)] PhysicalOwnerDirectoryV1 Directory,
    [property: Orleans.Id(5)] string PrincipalId,
    [property: Orleans.Id(6)] long PolicyEpoch,
    [property: Orleans.Id(7)] ImmutableArray<ResourceRetirementAcceptedMoveV1> SourceAcceptedMoves,
    [property: Orleans.Id(8)] ResourceRetirementPhaseReceiptV1 PrepareReceipt,
    [property: Orleans.Id(9)] string IntentDigest);
