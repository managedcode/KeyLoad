namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3RevocationHealthyCold
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantCapacityPlacementRf3RevocationCaller caller,
        PlacementIntentV1 completed, Guid id, ApplyPlacementIntentRequest request,
        PlacementIntentV1 oldIntent, Guid oldPersistId, TenantCapacityPlacementRf3RevocationCold.Rows oldRow, CancellationToken token)
    {
        var move = completed.Plan.Moves.Single();
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, move, before);
        await OldIntentAsync(wave, oldIntent, oldPersistId, oldRow);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await caller.ReconnectAsync(wave, token);
        await TenantCapacityPlacementRf3RevocationAssertions.ReplayAsync(caller, completed, id, request, token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await TenantCapacityPlacementRf3Cold.RequireCanonicalRowsAsync(state, before, after, completed.IntentId, move);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(after);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, move, after);
        await OldIntentAsync(wave, oldIntent, oldPersistId, oldRow);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await caller.ReconnectAsync(wave, token);
        await TenantCapacityPlacementRf3RevocationAssertions.ReplayAsync(caller, completed, id, request, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }

    private static async Task OldIntentAsync(TwoRf3MembershipWave wave, PlacementIntentV1 original, Guid persistId, TenantCapacityPlacementRf3RevocationCold.Rows rows)
    {
        var oldMove = original.Plan.Moves.Single();
        var old = TwoRf3MembershipProtocol.Nodes.Select(node =>
            PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, oldMove, Guid.Empty)).ToArray();
        await TenantCapacityPlacementRf3RevocationCold.RequirePendingAsync(old, original, persistId, rows);
    }
}
