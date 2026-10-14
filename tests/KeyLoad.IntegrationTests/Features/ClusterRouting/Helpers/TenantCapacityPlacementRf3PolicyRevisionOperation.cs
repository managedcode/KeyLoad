using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyRevisionOperation
{
    private const int FirstColdCut = 0;
    private const int RequiredColdCuts = 2;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        var plan = await TenantCapacityPlacementRf3PolicyRevisionSetup.RequireAsync(wave, seed, state, token);
        var repair = await TenantCapacityPlacementRf3PolicyRevision.RequireAsync(wave, seed, state, plan, token);
        var id = Guid.NewGuid();
        var request = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, repair.Plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(id, request, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, repair.Plan.Plan, request.IntentId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = id,
                    request
                }, token))).Value);
        await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, state, repair.Plan, pending, token);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(
            new(new(seed.Partition.TenantId, seed.Partition.DatabaseId, state.Control), pending.IntentId), token));
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        for (var cold = FirstColdCut; cold < RequiredColdCuts; cold++)
        {
            _ = await TenantCapacityPlacementRf3PolicyRevisionCold.RequireAsync(
                wave, seed, state, repair.Policy, repair.Original, repair.Row, token);
            await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state,
                completed.OriginalApplyCommandId!.Value, completed.OriginalApplyRequest!, completed, token);
        }
        await TenantCapacityPlacementRf3ExpiryAssertions.PersistReplayAsync(seed, id, request, pending, token);
    }
}
