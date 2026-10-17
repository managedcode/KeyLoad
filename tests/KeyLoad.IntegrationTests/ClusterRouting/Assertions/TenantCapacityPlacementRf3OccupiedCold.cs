using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedCold
{
    private const int RequiredColdCuts = 2;
    private const int FirstColdCut = 0;
    private const long NoOutstandingGrants = 0;
    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantDedicatedOwnerRf3State packed, PartitionMoveRequest move, PartitionMoveResult receipt,
        AtomicPartitionPlacementResolution sibling, long directoryRevision, CancellationToken token)
        => await RequireAsync(wave, seed, capacity, packed, move, receipt, sibling, directoryRevision,
            static _ => Task.CompletedTask, token);

    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantDedicatedOwnerRf3State packed, PartitionMoveRequest move, PartitionMoveResult receipt,
        AtomicPartitionPlacementResolution sibling, long directoryRevision,
        Func<PartitionMovementPublicParentRf3NativeCut[], Task> observe, CancellationToken token)
    {
        PartitionMovementPublicParentRf3NativeCut[]? original = null;
        for (var index = FirstColdCut; index < RequiredColdCuts; index++)
        {
            var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
            await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
            await observe(cuts);
            foreach (var cut in cuts)
            {
                await SqlRf3Protocol.EqualAsync(cut,
                    PartitionMovementPublicParentRf3Cut.ReadStopped(wave, cut.Node, move, Guid.Empty));
                await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(NoOutstandingGrants);
                await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(NoOutstandingGrants);
            }
            if (original is not null)
            {
                await TenantCapacityPlacementRf3OccupiedRows.RequireAsync(capacity, packed, original, cuts, move, token);
            }
            original = cuts;
            await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
            await seed.ReconnectAsync(wave, token);
            await TenantCapacityPlacementRf3OccupiedMoves.ReplayAsync(wave, move, receipt, token);
            await TenantCapacityPlacementRf3OccupiedAssertions.SiblingAsync(wave, packed, sibling, token);
            await TenantCapacityPlacementRf3OccupiedPlacement.RequireAsync(
                seed, capacity, packed, sibling, directoryRevision, token);
            await TenantCapacityPlacementRf3OccupiedAssertions.RefuseAsync(seed, capacity, token);
        }
    }
}
