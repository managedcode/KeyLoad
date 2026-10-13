namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiCold
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMoveRequest[] moves, Guid? id, ApplyPlacementIntentRequest? request,
        PlacementIntentV1? completed, CancellationToken token)
    {
        var before = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, moves, token);
        await OpenAsync(wave, moves, before, completed is null);
        await ReopenAsync(wave, seeds, token);
        await ReplayAsync(seeds, state, moves, id, request, completed, token);
        var after = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, moves, token);
        for (var index = 0; index < moves.Length; index++)
        {
            await TenantCapacityPlacementRf3Cold.RequireCanonicalRowsAsync(
                state, before[index], after[index], completed?.IntentId, moves[index]);
        }
        await OpenAsync(wave, moves, after, completed is null);
        await ReopenAsync(wave, seeds, token);
        await ReplayAsync(seeds, state, moves, id, request, completed, token);
    }

    private static async Task OpenAsync(TwoRf3MembershipWave wave, PartitionMoveRequest[] moves,
        PartitionMovementPublicParentRf3NativeCut[][] cuts, bool unstarted)
    {
        for (var index = 0; index < moves.Length; index++)
        {
            await Assert.That(cuts[index].Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
            if (unstarted)
            {
                foreach (var cut in cuts[index])
                {
                    await Assert.That(cut.Header).IsNull();
                    await Assert.That(cut.Pending).IsNull();
                    await Assert.That(cut.LastIssued).IsNull();
                }
            }
            await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts[index]);
            await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, moves[index], cuts[index]);
        }
    }

    private static async Task ReopenAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, CancellationToken token)
    {
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        foreach (var seed in seeds)
        { await seed.ReconnectAsync(wave, token); await seed.VerifyAsync(token); }
    }

    private static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest[] moves,
        Guid? id, ApplyPlacementIntentRequest? request, PlacementIntentV1? completed, CancellationToken token)
    {
        if (id is { } originalId && request is { } originalRequest && completed is { } original)
        {
            await TenantCapacityPlacementRf3MultiReplay.RequireAsync(
                seeds, state, moves, originalId, originalRequest, original, token);
            return;
        }
        foreach (var seed in seeds)
        { await TenantCapacityPlacementRf3Assertions.RequirePolicyReplayAsync(seed, state, token); }
    }
}
