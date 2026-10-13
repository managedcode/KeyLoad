namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyRevisionCold
{
    internal static async Task<string> RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        TenantPlacementPolicyV1 policy, TenantCapacityPlacementRf3PolicyRevision.Refused original,
        string? retained, CancellationToken token)
    {
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, original.Move, token);
        var row = await TenantCapacityPlacementRf3PolicyRevisionRows.RequireAsync(
            seed, state, policy, original, cuts, retained);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, original.Move, cuts);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        await TenantCapacityPlacementRf3PolicyRevisionAssertions.RefuseAsync(seed, original, token);
        return row;
    }
}
