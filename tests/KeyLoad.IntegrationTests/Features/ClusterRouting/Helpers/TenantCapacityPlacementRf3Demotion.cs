using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Demotion
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        var original = await TenantCapacityPlacementRf3DemotionAccounting.ReadAsync(wave, seed, token);
        var middle = await TenantCapacityPlacementRf3DemotionPolicy.MiddleAsync(seed, state, original.Bytes, token);
        var unchanged = await TenantCapacityPlacementRf3DemotionAccounting.ReadAsync(wave, seed, token);
        await SqlRf3Protocol.EqualAsync(original.Owner, unchanged.Owner);
        await Assert.That(unchanged.Bytes).IsEqualTo(original.Bytes);
        var equality = await TenantCapacityPlacementRf3DemotionPolicy.EqualDemotionAsync(
            seed, state, middle.Policy, original.Bytes, token);
        await TenantCapacityPlacementRf3DemotionAssertions.EmptyRefusalAsync(
            seed, middle.RefusedId, middle.Refused, token);
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var official = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new { request = query }, token))).Value;
        await TenantCapacityPlacementRf3DemotionAssertions.PlanAsync(
            seed, state, equality, official, moveExpected: true, token);
        var plan = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3DemotionAssertions.PlanAsync(
            seed, state, equality, plan, moveExpected: true, token);
        await TenantCapacityPlacementRf3DemotionApply.RunAsync(wave, seed, state, original.Owner,
            plan, middle.RefusedId, middle.Refused, token);
    }
}
