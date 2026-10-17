namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.FamilyAlias)]
internal sealed record PartitionMoveImageFamily(
    [property: Orleans.Id(0)] string Family,
    [property: Orleans.Id(1)] long RecordCount,
    [property: Orleans.Id(2)] long RawBytes,
    [property: Orleans.Id(3)] int PageCount,
    [property: Orleans.Id(4)] string Digest);
