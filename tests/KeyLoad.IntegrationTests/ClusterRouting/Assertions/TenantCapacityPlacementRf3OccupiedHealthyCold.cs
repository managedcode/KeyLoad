namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedHealthyCold
{
    private const int RequiredColdCuts = 2;
    private const int FirstColdCut = 0;
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantDedicatedOwnerRf3State packed,
        PartitionMoveRequest reverse, PartitionMoveResult reverseReceipt, PartitionMoveResult forwardReceipt,
        AtomicPartitionPlacementResolution sibling, TenantCapacityPlacementRf3OccupiedHealthyState healthy,
        CancellationToken token)
    {
        PartitionMovementPublicParentRf3NativeCut[]? original = null;
        for (var index = FirstColdCut; index < RequiredColdCuts; index++)
        {
            var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, reverse, token);
            await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
            await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, reverse, cuts);
            if (original is not null)
            {
                await TenantCapacityPlacementRf3OccupiedRows.RequireAsync(capacity, packed, original, cuts, reverse, token);
            }
            original = cuts;
            await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
            await seed.ReconnectAsync(wave, token);
            await TenantCapacityPlacementRf3OccupiedMoves.ReplayAsync(wave, packed.DeniedMove, forwardReceipt, token);
            await TenantCapacityPlacementRf3OccupiedMoves.ReplayAsync(wave, reverse, reverseReceipt, token);
            await TenantCapacityPlacementRf3OccupiedAssertions.SiblingAsync(wave, packed, sibling, token);
            await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
            await TenantCapacityPlacementRf3OccupiedHealthyAssertions.RequireAsync(wave, seed, packed, healthy, token);
        }
    }
}
