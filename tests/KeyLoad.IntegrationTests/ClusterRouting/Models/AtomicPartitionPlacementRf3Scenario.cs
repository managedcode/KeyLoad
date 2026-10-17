namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record AtomicPartitionPlacementRf3Scenario(PartitionRef Partition, PartitionRef FallbackPartition);
