using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3BoundedSelectionOperation
{
    private const int FirstColdCut = 0;
    private const int RequiredColdCuts = 2;
    private const long NewIntentRevision = 0;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, CancellationToken token)
    {
        _ = await TenantCapacityPlacementRf3PolicyRevisionSetup.RequireAsync(wave, seed, capacity, token);
        var before = await TenantCapacityPlacementRf3BoundedSelectionCold.RequireAsync(wave, seed, capacity, null, token);
        var request = TenantCapacityPlacementRf3BoundedSelectionAssertions.Request(seed, capacity);
        for (var cold = FirstColdCut; cold < RequiredColdCuts; cold++)
        {
            await TenantCapacityPlacementRf3BoundedSelectionAssertions.RefuseAsync(seed, capacity, request, token);
            _ = await TenantCapacityPlacementRf3BoundedSelectionCold.RequireAsync(wave, seed, capacity, before, token);
        }
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], capacity.Control);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new { request = query }, token))).Value;
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, capacity, sdk, token);
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, capacity, mcp, token);
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), NewIntentRevision, sdk.SignedPlan, capacity.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, sdk.Plan, persist.IntentId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new { commandId = persistId, request = persist }, token))).Value);
        await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, capacity, sdk, pending, token);
        await TenantCapacityPlacementRf3ExpiryAssertions.PersistReplayAsync(seed, persistId, persist, pending, token);
    }
}
