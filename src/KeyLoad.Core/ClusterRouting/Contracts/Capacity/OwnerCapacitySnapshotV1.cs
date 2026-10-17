using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnerCapacitySnapshotV1Aliases.OwnerCapacitySnapshotV1Alias)]
internal sealed record OwnerCapacitySnapshotV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(4)] string VoterId,
    [property: global::Orleans.Id(5)] string SiloAddress,
    [property: global::Orleans.Id(6)] string CohortFingerprint,
    [property: global::Orleans.Id(7)] OwnerResourceSampleV1 Resource,
    [property: global::Orleans.Id(8)] long StorePosition,
    [property: global::Orleans.Id(9)] long AppliedPosition,
    [property: global::Orleans.Id(10)] ImmutableArray<PartitionCapacityBytesV1> Partitions,
    [property: global::Orleans.Id(11)] long NativeReadBytes,
    [property: global::Orleans.Id(12)] int NativeExaminedRecords,
    [property: global::Orleans.Id(13)] ImmutableArray<AtomicPartitionCatalogEntryV1> CompleteRoster);
