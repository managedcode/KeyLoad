using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyRevision
{
    internal sealed record Refused(Guid Id, PersistPlacementIntentRequest Request, PartitionMoveRequest Move);
    internal static async Task<(TenantPlacementPlanResultV1 Plan, Refused Original, TenantPlacementPolicyV1 Policy, string Row)> RequireAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPlanResultV1 plan, CancellationToken token)
    {
        await Assert.That(plan.Plan.PolicyRevision).IsEqualTo(state.HealthyPolicy.Revision);
        var disabled = state.HealthyPolicy with
        {
            Revision = checked(state.HealthyPolicy.Revision + 1),
            Enabled = false
        };
        await ConfigureAsync(seed, state.Control, disabled, token);
        await Assert.That(plan.Plan.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        var original = new Refused(Guid.NewGuid(),
            new(Guid.NewGuid(), 0, plan.SignedPlan, state.Control), plan.Plan.Moves.Single());
        await TenantCapacityPlacementRf3PolicyRevisionAssertions.RefuseAsync(seed, original, token);
        var retained = await TenantCapacityPlacementRf3PolicyRevisionCold.RequireAsync(
            wave, seed, state, disabled, original, null, token);
        var repaired = state.HealthyPolicy with
        {
            Revision = checked(disabled.Revision + 1)
        };
        await ConfigureAsync(seed, state.Control, repaired, token);
        await TenantCapacityPlacementRf3PolicyRevisionAssertions.RefuseAsync(seed, original, token);
        _ = await TenantCapacityPlacementRf3PolicyRevisionCold.RequireAsync(
            wave, seed, state, repaired, original, retained, token);
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId, seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var official = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request = query
            }, token))).Value;
        var fresh = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        foreach (var current in new[] { official, fresh })
        {
            await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, current, token);
            await Assert.That(current.Plan.PolicyRevision).IsEqualTo(repaired.Revision);
            await Assert.That(current.Plan.TenantId).IsEqualTo(seed.Partition.TenantId);
            await Assert.That(current.Plan.DatabaseId).IsEqualTo(seed.Partition.DatabaseId);
            await Assert.That(current.Plan.OriginalPartitions.SequenceEqual(new[] { seed.Partition })).IsTrue();
            await Assert.That(current.SignedPlan).IsNotEqualTo(plan.SignedPlan);
        }
        return (fresh, original, repaired, retained);
    }
    private static async Task ConfigureAsync(PartitionMovementPublicParentRf3Seed seed,
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
}
