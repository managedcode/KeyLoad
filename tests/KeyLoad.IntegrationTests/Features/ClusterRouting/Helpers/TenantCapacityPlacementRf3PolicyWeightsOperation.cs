using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyWeightsOperation
{
    private const int AcceptedMinimumWeight = 1;
    private const int BelowMinimumWeight = 0;
    private const int AboveMaximumWeight = 17;
    private const int AcceptedMaximumWeight = 16;
    private const long NextPolicyRevision = 3;
    internal sealed record Refused(Guid Id, ConfigurePlacementPolicyRequest Request);
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        var plan = await TenantCapacityPlacementRf3PolicyRevisionSetup.RequireAsync(wave, seed, state, token);
        foreach (var owner in state.HealthyPolicy.OwnerWeights)
        {
            await Assert.That(owner.Weight).IsEqualTo(AcceptedMinimumWeight);
        }
        var refused = new List<Refused>();
        foreach (var invalid in new[] { BelowMinimumWeight, AboveMaximumWeight })
        {
            var candidate = state.HealthyPolicy with
            {
                Revision = NextPolicyRevision,
                OwnerWeights = state.HealthyPolicy.OwnerWeights.Select(owner =>
                    owner.PhysicalShardId == seed.FirstRequest.DestinationPhysicalShardId
                        ? owner with
                        {
                            Weight = invalid
                        } : owner).ToImmutableArray()
            };
            refused.Add(new(Guid.NewGuid(), new(state.HealthyPolicy.Revision, candidate, state.Control)));
        }
        await TenantCapacityPlacementRf3PolicyWeightsAssertions.RefuseAsync(seed, refused, token);
        var rows = await TenantCapacityPlacementRf3PolicyWeightsCold.RequireAsync(wave, seed, state,
            state.HealthyPolicy, refused, null, plan.Plan.Moves.Single(), absentMovement: true, token);
        rows = await TenantCapacityPlacementRf3PolicyWeightsCold.RequireAsync(wave, seed, state,
            state.HealthyPolicy, refused, rows, plan.Plan.Moves.Single(), absentMovement: true, token);
        var repaired = state.HealthyPolicy with
        {
            Revision = NextPolicyRevision,
            OwnerWeights = state.HealthyPolicy.OwnerWeights.Select(owner =>
                owner.PhysicalShardId == seed.FirstRequest.DestinationPhysicalShardId
                    ? owner with
                    {
                        Weight = AcceptedMaximumWeight
                    } : owner).ToImmutableArray()
        };
        var accepted = await TenantCapacityPlacementRf3PolicyWeightsAuthority.ConfigureAsync(seed, state.Control, repaired, token);
        await TenantCapacityPlacementRf3PolicyWeightsAssertions.RefuseAsync(seed, refused, token);
        await TenantCapacityPlacementRf3PolicyWeightsHealthy.RunAsync(
            wave, seed, state, repaired, refused, rows, accepted, token);
    }
}
