using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PendingPolicyAuthority
{
    internal static async Task ConfigureAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionRef control, TenantPlacementPolicyV1 policy, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new ConfigurePlacementPolicyRequest(checked(policy.Revision - 1), policy, control);
        await SqlRf3Protocol.EqualAsync(policy, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigurePlacementPolicyAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(policy, (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.ConfigureTool,
                new
                {
                    commandId = id,
                    request
                }, token))).Value);
    }

    internal static async Task PersistReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid id, PersistPlacementIntentRequest request, PlacementIntentV1 pending, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(pending, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = id,
                    request
                }, token))).Value);
    }

    internal static async Task<TenantPlacementPlanResultV1> PlanAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy, CancellationToken token)
    {
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var official = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request = query
            }, token))).Value;
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        foreach (var current in new[] { official, sdk })
        {
            await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, current, token);
            await Assert.That(current.Plan.PolicyRevision).IsEqualTo(policy.Revision);
        }
        return sdk;
    }
}
