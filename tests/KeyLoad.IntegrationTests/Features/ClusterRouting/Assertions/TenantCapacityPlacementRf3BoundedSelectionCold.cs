namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3BoundedSelectionCold
{
    internal static async Task<PartitionMovementPublicParentRf3NativeCut[]> RequireAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, PartitionMovementPublicParentRf3NativeCut[]? retained,
        CancellationToken token)
    {
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        foreach (var cut in cuts)
        {
            await Assert.That(cut.Header).IsNull();
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.LastIssued).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(0L);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(0L);
        }
        if (retained is not null)
        { await TenantCapacityPlacementRf3Cold.RequireCanonicalRowsAsync(capacity, retained, cuts, null, seed.FirstRequest); }
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, seed.FirstRequest, cuts);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
        return cuts;
    }
}
