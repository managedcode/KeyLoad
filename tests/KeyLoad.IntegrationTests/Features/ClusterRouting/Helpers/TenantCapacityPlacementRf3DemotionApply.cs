using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3DemotionApply
{
    private const long EmptyRevision = 0;
    private const long SingleClaimAndReceiptRevision = 3;
    private const long NextPublication = 3;
    private const int SingleMove = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, AtomicPartitionPlacementResolution original,
        TenantPlacementPlanResultV1 plan, Guid refusedId, PersistPlacementIntentRequest refused, CancellationToken token)
    {
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), EmptyRevision, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, persist.IntentId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new { commandId = persistId, request = persist }, token))).Value);
        var id = Guid.NewGuid();
        var request = new ApplyPlacementIntentRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            pending.IntentId, pending.Revision, state.Control);
        var completed = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ApplyPlacementIntentAsync(id, request, token));
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        await Assert.That(completed.CompletedMoves).IsEqualTo(SingleMove);
        await Assert.That(completed.Revision).IsEqualTo(SingleClaimAndReceiptRevision);
        await Assert.That(completed.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(completed.OriginalApplyRequest).IsEqualTo(request);
        await Assert.That(completed.PrincipalId).IsEqualTo(pending.PrincipalId);
        await Assert.That(completed.PolicyEpoch).IsEqualTo(pending.PolicyEpoch);
        await Assert.That(completed.Claim).IsNotNull();
        await SqlRf3Protocol.EqualAsync(pending.Plan, completed.Plan);
        await SqlRf3Protocol.EqualAsync(pending.ControlOwner, completed.ControlOwner);
        var move = plan.Plan.Moves.Single();
        var receipt = completed.Receipts.Single();
        var source = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId == original.PhysicalShardId).Owner;
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed, move, original, source, receipt,
            NextPublication, NextPublication, token);
        await TenantCapacityPlacementRf3DemotionReplay.RequireAsync(seed, state, plan, pending,
            persistId, persist, id, request, completed, refusedId, refused, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(await seed.Source.ApplyPlacementIntentAsync(
            id, request with { ExpectedRevision = completed.Revision }, token), ErrorCode.Conflict);
        await TenantCapacityPlacementRf3Cold.RequireHealthyReplayAsync(wave, seed, state,
            move, id, request, completed, token);
        await TenantCapacityPlacementRf3DemotionReplay.RequireAsync(seed, state, plan, pending,
            persistId, persist, id, request, completed, refusedId, refused, token);
        await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
    }
}
