namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record TenantPackingActivationRf3State(
    RequestCqrsPhaseFaultIdentity First,
    RequestCqrsPhaseFaultIdentity Second,
    CommandRequest FirstSeed,
    CommandRequest SecondSeed,
    CommitReceipt FirstReceipt,
    CommitReceipt SecondReceipt,
    AtomicPartitionPlacementResolution FirstPlacement,
    AtomicPartitionPlacementResolution SecondPlacement);
