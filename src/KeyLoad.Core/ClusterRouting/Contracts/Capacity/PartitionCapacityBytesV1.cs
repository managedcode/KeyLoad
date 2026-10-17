namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnerCapacitySnapshotV1Aliases.PartitionCapacityBytesV1Alias)]
internal sealed record PartitionCapacityBytesV1(
    [property: global::Orleans.Id(0)] PartitionRef Partition,
    [property: global::Orleans.Id(1)] long Bytes);
