using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionRepair
{
    private const int FirstColdCut = 0;
    private const int RequiredColdCuts = 2;
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantCapacityPlacementRf3IntentCollisionState original,
        TenantCapacityPlacementRf3IntentCollisionRowsState rows, CancellationToken token)
    {
        var plan = await TenantCapacityPlacementRf3IntentCollisionPersistence.PlanAsync(seed, capacity, token);
        var id = Guid.NewGuid();
        var request = TenantCapacityPlacementRf3IntentCollisionPersistence.Request(Guid.NewGuid(), plan, capacity);
        await Assert.That(request.IntentId).IsNotEqualTo(original.Request.IntentId);
        var pending = await TenantCapacityPlacementRf3IntentCollisionPersistence.PersistAsync(seed, id, request, plan, token);
        await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, capacity, plan, pending, token);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(
            new(new(seed.Partition.TenantId, seed.Partition.DatabaseId, capacity.Control), pending.IntentId), token));
        await Assert.That(completed.Status).IsEqualTo(PlacementIntentStatus.Completed);
        for (var cold = FirstColdCut; cold < RequiredColdCuts; cold++)
        {
            _ = await TenantCapacityPlacementRf3IntentCollisionCold.RequireAsync(wave, seed, capacity, original, rows, token);
            await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, capacity,
                completed.OriginalApplyCommandId!.Value, completed.OriginalApplyRequest!, completed, token);
        }
        await TenantCapacityPlacementRf3ExpiryAssertions.PersistReplayAsync(seed, id, request, pending, token);
    }
}
