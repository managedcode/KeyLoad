using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Expiry
{
    internal static async Task<TenantPlacementPlanResultV1> RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        Guid commandId, Guid intentId, CancellationToken token)
    {
        var baseline = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, seed.FirstRequest, baseline);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var original = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PlanTenantPlacementAsync(query, token));
        var receivedAt = TimeProvider.System.GetUtcNow();
        await Assert.That(original.Plan.ExpiresAt).IsGreaterThan(receivedAt);
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, original, token);
        var cohort = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var sampleCeiling = receivedAt.AddSeconds(TenantPlacementExecutionOptions.DefaultFreshnessSeconds);
        var originalDeadline = original.Plan.ExpiresAt > sampleCeiling ? original.Plan.ExpiresAt : sampleCeiling;
        await WaitForOriginalExpiryAsync(originalDeadline, token);
        await SqlRf3Protocol.EqualAsync(cohort,
            await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token));
        var request = new PersistPlacementIntentRequest(intentId, 0, original.SignedPlan, state.Control);
        await TenantCapacityPlacementRf3ExpiryAssertions.RefuseAsync(seed, commandId, request, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        await TenantCapacityPlacementRf3ExpiryAssertions.ColdAsync(wave, seed, state, baseline,
            commandId, intentId, token);
        var repaired = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, repaired, token);
        await Assert.That(repaired.Plan.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await Assert.That(repaired.SignedPlan).IsNotEqualTo(original.SignedPlan);
        return repaired;
    }

    private static async Task WaitForOriginalExpiryAsync(DateTimeOffset deadline, CancellationToken token)
    {
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var remaining = deadline - TimeProvider.System.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            { await Task.Delay(remaining, TimeProvider.System, token).ConfigureAwait(false); }
        }
    }
}
