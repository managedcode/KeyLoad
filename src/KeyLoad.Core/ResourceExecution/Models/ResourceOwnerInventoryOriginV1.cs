namespace KeyLoad.Core.Features.ResourceExecution;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryProtocol.OriginAlias)]
internal sealed record ResourceOwnerInventoryOriginV1(
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.FirstField)] int Version,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.SecondField)] Guid StoreIncarnation,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.ThirdField)] bool Proven,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.FourthField)] long Revision);
