namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiReplay
{
    private const int CompletedCount = 2;
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest[] moves,
        Guid id, ApplyPlacementIntentRequest request, PlacementIntentV1 original, CancellationToken token)
    {
        await Assert.That(original.Status).IsEqualTo(PlacementIntentStatus.Completed);
        await Assert.That(original.CompletedMoves).IsEqualTo(CompletedCount);
        await Assert.That(original.Receipts.Length).IsEqualTo(CompletedCount);
        await Assert.That(original.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(original.OriginalApplyRequest).IsEqualTo(request);
        await Assert.That(original.Claim).IsNotNull();
        for (var index = 0; index < moves.Length; index++)
        {
            var move = moves[index];
            var receipt = original.Receipts[index];
            var seed = seeds.Single(value => value.Partition == move.Partition);
            await Assert.That(receipt.MoveId).IsEqualTo(move.MoveId);
            await Assert.That(receipt.Partition).IsEqualTo(move.Partition);
            await Assert.That(receipt.Phase).IsEqualTo(PartitionMovePhase.Retired);
            await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, move, receipt, token);
            await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state, id, request, original, token);
        }
        await TenantCapacityPlacementRf3MultiAssertions.PlacementsAsync(seeds[0], state, moves, CompletedCount, token);
    }
}
