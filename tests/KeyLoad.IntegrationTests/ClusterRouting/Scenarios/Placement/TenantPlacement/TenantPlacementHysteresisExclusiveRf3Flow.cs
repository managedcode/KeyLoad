namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Flow
{
    private const long OriginalDirectory = 6;
    private const long OccupiedDirectory = 7;
    private const long RepairedDirectory = 8;
    private const long ForeignOriginalRow = 5;
    private const long PolicyRevision = 3;
    private const long DemotionBytes = 0;

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantDedicatedOwnerRf3State packed,
        TenantCapacityPlacementRf3OccupiedHealthyState healthy,
        CancellationToken token)
    {
        var policy = capacity.HealthyPolicy with
        {
            Revision = PolicyRevision,
            PromotionBytes = long.MaxValue,
            DemotionBytes = DemotionBytes
        };
        var state = new TenantPlacementHysteresisExclusiveRf3State(wave, seed, capacity, packed,
            healthy, Guid.NewGuid(), new(capacity.HealthyPolicy.Revision, policy, capacity.Control), OriginalDirectory);
        await TenantPlacementHysteresisExclusiveRf3Policy.ConfigureAsync(state, token);
        await TenantPlacementHysteresisExclusiveRf3Plans.RequireAsync(state, token);
        var forward = new PartitionMoveRequest(Guid.NewGuid(), packed.Sibling.Partition,
            state.Target.PhysicalShardId, ForeignOriginalRow, PartitionMoveMode.Transfer);
        var occupied = state with
        {
            DirectoryRevision = OccupiedDirectory
        };
        var forwardReceipt = await TenantPlacementHysteresisExclusiveRf3Moves.RunAsync(occupied, forward, token);
        await TenantPlacementHysteresisExclusiveRf3Plans.RequireAsync(occupied, token);
        await TenantPlacementHysteresisExclusiveRf3Cold.RequireAsync(occupied, forward, forwardReceipt, token);
        var reverse = new PartitionMoveRequest(Guid.NewGuid(), packed.Sibling.Partition,
            seed.Directory.ControlOwner.PhysicalShardId, OccupiedDirectory, PartitionMoveMode.Transfer);
        var repaired = state with
        {
            DirectoryRevision = RepairedDirectory
        };
        var reverseReceipt = await TenantPlacementHysteresisExclusiveRf3Moves.RunAsync(repaired, reverse, token);
        await TenantPlacementHysteresisExclusiveRf3Moves.ReplayAsync(repaired, forward, forwardReceipt, token);
        await TenantPlacementHysteresisExclusiveRf3Plans.RequireAsync(repaired, token);
        await TenantPlacementHysteresisExclusiveRf3Cold.RequireAsync(repaired, reverse, reverseReceipt, token);
        await TenantPlacementHysteresisExclusiveRf3Moves.ReplayAsync(repaired, forward, forwardReceipt, token);
    }
}
