namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionCold
{
    internal static async Task<TenantCapacityPlacementRf3IntentCollisionRowsState> RequireAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantCapacityPlacementRf3IntentCollisionState original,
        TenantCapacityPlacementRf3IntentCollisionRowsState? retained, CancellationToken token)
    {
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, original.Move, token);
        var rows = await TenantCapacityPlacementRf3IntentCollisionRows.RequireAsync(seed, capacity, original, cuts, retained);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, original.Move, cuts);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
        await TenantCapacityPlacementRf3IntentCollisionAssertions.PendingAsync(seed, original, token);
        if (original.CollisionId != Guid.Empty)
        { await TenantCapacityPlacementRf3IntentCollisionAssertions.RefuseAsync(seed, original, token); }
        return rows;
    }
}
