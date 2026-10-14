using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyWeightsHealthy
{
    private const int RequiredColdCuts = 2;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        IReadOnlyList<TenantCapacityPlacementRf3PolicyWeightsOperation.Refused> original,
        TenantCapacityPlacementRf3PolicyWeightsRows.Rows rows,
        TenantCapacityPlacementRf3PolicyWeightsAuthority.Accepted accepted,
        TenantCapacityPlacementRf3PolicyWeightsAuthority.Accepted restricted, CancellationToken token)
    {
        await TenantCapacityPlacementRf3ConfiguredOwners.ReplayAsync(seed, state, restricted, policy, token);
        var plan = await TenantCapacityPlacementRf3PolicyWeightsAuthority.PlanAsync(seed, state, policy, token);
        var id = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PersistPlacementIntentAsync(id, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, persist.IntentId);
        await TenantCapacityPlacementRf3PolicyWeightsAuthority.PersistReplayAsync(seed, id, persist, pending, token);
        await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, state, plan, pending, token);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(
            new(new(seed.Partition.TenantId, seed.Partition.DatabaseId, state.Control), pending.IntentId), token));
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        for (var cold = 0; cold < RequiredColdCuts; cold++)
        {
            rows = await TenantCapacityPlacementRf3PolicyWeightsCold.RequireAsync(wave, seed, state,
                policy, original, rows, plan.Plan.Moves.Single(), absentMovement: false, token);
            await TenantCapacityPlacementRf3PolicyWeightsAuthority.ConfigureReplayAsync(seed, accepted, policy, token);
            await TenantCapacityPlacementRf3ConfiguredOwners.ReplayAsync(seed, state, restricted, policy, token);
            await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state,
                completed.OriginalApplyCommandId!.Value, completed.OriginalApplyRequest!, completed, token);
        }
        await TenantCapacityPlacementRf3PolicyWeightsAuthority.PersistReplayAsync(seed, id, persist, pending, token);
        await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
    }
}
