namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnerCapacityReadRequestV1Aliases.OwnerResourceSampleV1Alias)]
internal sealed record OwnerResourceSampleV1(
    [property: global::Orleans.Id(0)] long Sequence,
    [property: global::Orleans.Id(1)] DateTimeOffset SampledAt,
    [property: global::Orleans.Id(2)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(3)] long AvailableDiskBytes,
    [property: global::Orleans.Id(4)] long TotalDiskBytes,
    [property: global::Orleans.Id(5)] long MemoryCurrentBytes,
    [property: global::Orleans.Id(6)] long MemoryLimitBytes,
    [property: global::Orleans.Id(7)] int CpuBasisPoints,
    [property: global::Orleans.Id(8)] string? HostDomain,
    [property: global::Orleans.Id(9)] string ProcessDomain);
