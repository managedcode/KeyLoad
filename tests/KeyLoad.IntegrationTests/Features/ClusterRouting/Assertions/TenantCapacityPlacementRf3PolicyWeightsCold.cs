
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyWeightsCold
{
    internal static async Task<TenantCapacityPlacementRf3PolicyWeightsRows.Rows> RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        TenantPlacementPolicyV1 policy, IReadOnlyList<TenantCapacityPlacementRf3PolicyWeightsOperation.Refused> original,
        TenantCapacityPlacementRf3PolicyWeightsRows.Rows? retained, PartitionMoveRequest observedMove,
        bool absentMovement, CancellationToken token)
    {
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, observedMove, token);
        if (absentMovement)
        {
            foreach (var cut in cuts)
            {
                await Assert.That(cut.Header).IsNull();
                await Assert.That(cut.Pending).IsNull();
                await Assert.That(cut.LastIssued).IsNull();
            }
        }
        var rows = await TenantCapacityPlacementRf3PolicyWeightsRows.RequireAsync(seed, state, policy, original, cuts, retained);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, observedMove, cuts);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        await TenantCapacityPlacementRf3PolicyWeightsAssertions.RefuseAsync(seed, original, token);
        return rows;
    }
}
