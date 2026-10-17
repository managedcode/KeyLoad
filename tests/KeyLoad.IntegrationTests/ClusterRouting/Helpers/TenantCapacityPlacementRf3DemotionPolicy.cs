using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3DemotionPolicy
{
    private const long RevisionStep = 1;

    internal static async Task<(TenantPlacementPolicyV1 Policy, Guid RefusedId, PersistPlacementIntentRequest Refused)> MiddleAsync(
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        long bytes, CancellationToken token)
    {
        var middle = state.HealthyPolicy with
        {
            Revision = checked(state.HealthyPolicy.Revision + RevisionStep),
            PromotionBytes = checked(bytes + RevisionStep),
            DemotionBytes = checked(bytes - RevisionStep)
        };
        await ConfigureAsync(seed, state.Control, middle, token);
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var official = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request = query
            }, token))).Value;
        await TenantCapacityPlacementRf3DemotionAssertions.PlanAsync(seed, state, middle, official, moveExpected: false, token);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3DemotionAssertions.PlanAsync(seed, state, middle, sdk, moveExpected: false, token);
        var id = Guid.NewGuid();
        var request = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, sdk.SignedPlan, state.Control);
        await TenantCapacityPlacementRf3DemotionAssertions.EmptyRefusalAsync(seed, id, request, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        return (middle, id, request);
    }

    internal static async Task<TenantPlacementPolicyV1> EqualDemotionAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 middle,
        long bytes, CancellationToken token)
    {
        var equality = middle with
        {
            Revision = checked(middle.Revision + RevisionStep),
            DemotionBytes = bytes
        };
        await ConfigureAsync(seed, state.Control, equality, token);
        return equality;
    }

    private static async Task ConfigureAsync(PartitionMovementPublicParentRf3Seed seed, PartitionRef control,
        TenantPlacementPolicyV1 policy, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new ConfigurePlacementPolicyRequest(checked(policy.Revision - RevisionStep), policy, control);
        await SqlRf3Protocol.EqualAsync(policy,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigurePlacementPolicyAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(policy,
            (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.ConfigureTool, new
                {
                    commandId = id,
                    request
                }, token))).Value);
    }
}
