using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PhysicalShardCatalogInterface34Baseline(NodeEpochRf3Profile Profile, byte[] ProfileBytes,
    RequestCqrsRf3Workload Workload, NodeEpochRf3Inventory[] NodeInventories);
