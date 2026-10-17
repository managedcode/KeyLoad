using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Apply
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPlanResultV1 plan,
        PlacementIntentV1 pending, CancellationToken token)
    {
        var request = new ApplyPlacementIntentRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            pending.IntentId, pending.Revision, state.Control);
        var id = Guid.NewGuid();
        var completed = await TenantCapacityPlacementRf3ClaimRace.RunAsync(
            wave, seed, plan, pending, id, request, token);
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        await Assert.That(completed.CompletedMoves).IsEqualTo(1);
        await Assert.That(completed.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(completed.OriginalApplyRequest).IsEqualTo(request);
        var original = completed.Receipts.Single();
        var move = plan.Plan.Moves.Single();
        await Assert.That(original.MoveId).IsEqualTo(move.MoveId);
        await Assert.That(original.Partition).IsEqualTo(seed.Partition);
        await Assert.That(original.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, move, original, token);
        await RequireReplayAsync(seed, state, id, request, completed, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await seed.Source.ApplyPlacementIntentAsync(id, request with
            {
                ExpectedRevision = completed.Revision
            }, token),
            ErrorCode.Conflict);
        await RequireReplayAsync(seed, state, id, request, completed, token);
        await TenantCapacityPlacementRf3Cold.RequireHealthyReplayAsync(wave, seed, state,
            move, id, request, completed, token);
    }

    internal static async Task RequireReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, Guid id, ApplyPlacementIntentRequest request,
        PlacementIntentV1 original, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ApplyPlacementIntentAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(original,
            (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
                await seed.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
                    new
                    {
                        commandId = id,
                        request
                    }, token))).Value);
        await SqlRf3Protocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(
                new(new(seed.Partition.TenantId, seed.Partition.DatabaseId, state.Control), original.IntentId), token)));
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }
}
