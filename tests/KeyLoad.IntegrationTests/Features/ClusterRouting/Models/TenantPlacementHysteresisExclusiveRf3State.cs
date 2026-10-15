namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record TenantPlacementHysteresisExclusiveRf3State(
    TwoRf3MembershipWave Wave,
    PartitionMovementPublicParentRf3Seed Seed,
    TenantCapacityPlacementRf3Provisioning.State Capacity,
    TenantDedicatedOwnerRf3State Packed,
    TenantCapacityPlacementRf3OccupiedHealthyState Healthy,
    Guid PolicyId,
    ConfigurePlacementPolicyRequest Configure,
    long DirectoryRevision)
{
    internal TenantPlacementPolicyV1 Policy => Configure.Policy;
    internal TenantPlacementPlanRequest PlanRequest => new(Seed.Partition.TenantId,
        Seed.Partition.DatabaseId, [Seed.Partition], Capacity.Control);
    internal PhysicalShardRecord Target => Seed.Directory.Owners.Single(entry =>
        entry.Owner.PhysicalShardId == Seed.FirstRequest.DestinationPhysicalShardId).Owner;
}
