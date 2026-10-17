namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementRf3ScenarioFactory
{
    internal static AtomicPartitionPlacementRf3Scenario Create()
    {
        var bound = new PartitionRef(AtomicPartitionPlacementPublicRf3Protocol.Tenant,
            AtomicPartitionPlacementPublicRf3Protocol.Database, AtomicPartitionPlacementPublicRf3Protocol.Domain,
            AtomicPartitionPlacementPublicRf3Protocol.PartitionId);
        var fallback = new PartitionRef(AtomicPartitionPlacementPublicRf3Protocol.Tenant,
            AtomicPartitionPlacementPublicRf3Protocol.Database, AtomicPartitionPlacementPublicRf3Protocol.Domain,
            AtomicPartitionPlacementPublicRf3Protocol.FallbackPartitionId);
        return new(bound, fallback);
    }
}
