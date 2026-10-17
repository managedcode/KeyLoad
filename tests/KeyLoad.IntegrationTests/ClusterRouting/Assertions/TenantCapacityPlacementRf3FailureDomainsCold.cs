using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3FailureDomainsCold
{
    internal static async Task<TenantCapacityPlacementRf3FailureDomainsRows.Rows> RequireAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        TenantCapacityPlacementRf3FailureDomainsOperation.Original original, PlacementIntentV1 applying,
        string? persistRow, PartitionMoveRequest observedMove, bool absentParent, CancellationToken token)
    {
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, observedMove, token);
        if (absentParent)
        {
            foreach (var cut in stopped)
            {
                await Assert.That(cut.Header).IsNull();
                await Assert.That(cut.Pending).IsNull();
                await Assert.That(cut.LastIssued).IsNull();
            }
        }
        var rows = await TenantCapacityPlacementRf3FailureDomainsRows.RequireAsync(
            stopped, original, applying, policy, persistRow);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, observedMove, stopped);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(stopped);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await SqlRf3Protocol.EqualAsync(applying,
            await TenantCapacityPlacementRf3FailureDomainsAssertions.ReadAsync(seed, original, token));
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        return rows;
    }
}
