using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnerCapacityReadRequestV1Aliases.OwnerCapacityReadRequestV1Alias)]
internal sealed record OwnerCapacityReadRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(4)] string VoterId,
    [property: global::Orleans.Id(5)] string SiloAddress,
    [property: global::Orleans.Id(6)] string CohortFingerprint,
    [property: global::Orleans.Id(7)] DateTimeOffset OriginalExpiry,
    [property: global::Orleans.Id(8)] ImmutableArray<PartitionRef> Partitions,
    [property: global::Orleans.Id(9)] OwnerResourceSampleV1 Resource,
    [property: global::Orleans.Id(10)] long OriginalPolicyEpoch,
    [property: global::Orleans.Id(11)] long MaximumReadBytes,
    [property: global::Orleans.Id(12)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(13)] int MaximumResultBytes);
