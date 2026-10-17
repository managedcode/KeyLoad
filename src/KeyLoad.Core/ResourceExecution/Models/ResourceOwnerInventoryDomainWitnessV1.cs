namespace KeyLoad.Core.Features.ResourceExecution;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryProtocol.WitnessAlias)]
internal sealed record ResourceOwnerInventoryDomainWitnessV1(
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.FirstField)] Guid StoreIncarnation,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.SecondField)] long DomainRevision,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.ThirdField)] string CoverageDigest);
