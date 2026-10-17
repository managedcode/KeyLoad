using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PendingPolicyOperation
{
    internal sealed record Original(PlacementIntentV1 Pending, Guid PersistId,
        PersistPlacementIntentRequest Persist, Guid ApplyId, ApplyPlacementIntentRequest Apply);

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        var plan = await TenantCapacityPlacementRf3PolicyRevisionSetup.RequireAsync(wave, seed, state, token);
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, persist.IntentId);
        await TenantCapacityPlacementRf3PendingPolicyAuthority.PersistReplayAsync(seed, persistId, persist, pending, token);
        var original = new Original(pending, persistId, persist, Guid.NewGuid(),
            new(seed.Partition.TenantId, seed.Partition.DatabaseId, pending.IntentId, pending.Revision, state.Control));
        var baseline = await TenantCapacityPlacementRf3PendingPolicyCold.RequireAsync(
            wave, seed, state, state.HealthyPolicy, original, pending, null,
            pending.Plan.Moves.Single(), true, token);
        var disabled = state.HealthyPolicy with
        {
            Revision = 3,
            Enabled = false
        };
        await TenantCapacityPlacementRf3PendingPolicyAuthority.ConfigureAsync(seed, state.Control, disabled, token);
        var applying = await TenantCapacityPlacementRf3PendingPolicyAssertions.RefuseAsync(
            wave, seed, original, expectedRevision: 2, token);
        var rows = await TenantCapacityPlacementRf3PendingPolicyCold.RequireAsync(
            wave, seed, state, disabled, original, applying, baseline.Persist, pending.Plan.Moves.Single(), true, token);
        var repaired = state.HealthyPolicy with
        {
            Revision = 4
        };
        await TenantCapacityPlacementRf3PendingPolicyAuthority.ConfigureAsync(seed, state.Control, repaired, token);
        var renewed = await TenantCapacityPlacementRf3PendingPolicyAssertions.RefuseAsync(
            wave, seed, original, expectedRevision: 3, token);
        await Assert.That(renewed.Claim!.Nonce).IsNotEqualTo(applying.Claim!.Nonce);
        await Assert.That(renewed.Claim.CohortFingerprint).IsNotEqualTo(applying.Claim.CohortFingerprint);
        var current = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        await Assert.That(current.Any(value => value.SiloAddress == applying.Claim.SiloAddress)).IsFalse();
        await TenantCapacityPlacementRf3PendingPolicyAuthority.PersistReplayAsync(seed, persistId, persist, pending, token);
        rows = await TenantCapacityPlacementRf3PendingPolicyCold.RequireAsync(
            wave, seed, state, repaired, original, renewed, rows.Persist, pending.Plan.Moves.Single(), true, token);
        await TenantCapacityPlacementRf3PendingPolicyHealthy.RunAsync(
            wave, seed, state, repaired, original, renewed, rows, token);
    }
}
