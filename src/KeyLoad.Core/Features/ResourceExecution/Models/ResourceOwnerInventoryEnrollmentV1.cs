namespace KeyLoad.Core.Features.ResourceExecution;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryProtocol.EnrollmentAlias)]
internal sealed record ResourceOwnerInventoryEnrollmentV1(
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.FirstField)] int Version,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.SecondField)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.ThirdField)] PartitionRef Partition,
    [property: Orleans.Id(ResourceOwnerInventoryProtocol.FourthField)] ResourceOwnerInventoryOriginV1 Origin);
