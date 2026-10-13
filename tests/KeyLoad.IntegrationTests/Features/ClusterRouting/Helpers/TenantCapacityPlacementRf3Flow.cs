using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
    {
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(await seed.Source.PlanTenantPlacementAsync(query, token),
            ErrorCode.ResourceExhausted);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        var dirty = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireDirtyReplayAsync(wave, seed, state, dirty, token);
        await TenantCapacityPlacementRf3MissingMember.RequireAsync(wave, seed, state, query, dirty, token);
        var healthy = new ConfigurePlacementPolicyRequest(state.OriginalPolicy.Policy.Revision,
            state.HealthyPolicy, state.Control);
        _ = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigurePlacementPolicyAsync(
            Guid.NewGuid(), healthy, token));
        var plan = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        await TenantCapacityPlacementRf3Authorization.RequireAsync(wave, seed, state, plan, token);
        plan = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        var intentId = Guid.NewGuid();
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(intentId, 0, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, intentId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new { commandId = persistId, request = persist }, token))).Value);
        await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, state, plan, pending, token);
    }
}
