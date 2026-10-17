using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3DemotionReplay
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPlanResultV1 plan, PlacementIntentV1 pending,
        Guid persistId, PersistPlacementIntentRequest persist, Guid id, ApplyPlacementIntentRequest request,
        PlacementIntentV1 completed, Guid refusedId, PersistPlacementIntentRequest refused, CancellationToken token)
    {
        await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state, id, request, completed, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official,
            plan.Plan.Moves.Single(), completed.Receipts.Single(), token);
        await SqlRf3Protocol.EqualAsync(pending,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PersistPlacementIntentAsync(persistId, persist, token)));
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = persistId,
                    request = persist
                }, token))).Value);
        await TenantCapacityPlacementRf3DemotionAssertions.EmptyRefusalAsync(seed, refusedId, refused, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }
}
