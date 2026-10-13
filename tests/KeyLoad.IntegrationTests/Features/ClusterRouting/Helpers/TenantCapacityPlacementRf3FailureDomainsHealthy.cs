using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3FailureDomainsHealthy
{
    private const int RequiredColdCuts = 2;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        TenantCapacityPlacementRf3FailureDomainsOperation.Original original, PlacementIntentV1 applying,
        TenantCapacityPlacementRf3FailureDomainsRows.Rows originalRows,
        IReadOnlyList<TenantCapacityPlacementRf3FailureDomainsAuthority.Accepted> policies, CancellationToken token)
    {
        var plan = await TenantCapacityPlacementRf3FailureDomainsAuthority.PlanAsync(seed, state, policy, token);
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, persist.IntentId);
        await Assert.That(pending.IntentId).IsNotEqualTo(original.Pending.IntentId);
        await TenantCapacityPlacementRf3FailureDomainsAuthority.PersistReplayAsync(seed, persistId, persist, pending, token);
        var id = Guid.NewGuid();
        var request = new ApplyPlacementIntentRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, pending.IntentId, pending.Revision, state.Control);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ApplyPlacementIntentAsync(id, request, token));
        await TenantCapacityPlacementRf3RevocationAssertions.CompletedAsync(pending, completed, id, request);
        var move = plan.Plan.Moves.Single();
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed, move, seed.OriginalPlacement with
        { DirectoryRevision = 1 }, seed.Directory.ControlOwner, completed.Receipts.Single(), 2, 2, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, move, completed.Receipts.Single(), token);
        await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state, id, request, completed, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(await seed.Source.ApplyPlacementIntentAsync(
            id, request with { ExpectedRevision = completed.Revision }, token), ErrorCode.Conflict);
        for (var cold = 0; cold < RequiredColdCuts; cold++)
        {
            foreach (var historical in policies)
            { await TenantCapacityPlacementRf3FailureDomainsAuthority.ConfigureReplayAsync(seed, historical, token); }
            var rows = await TenantCapacityPlacementRf3FailureDomainsCold.RequireAsync(wave, seed, state, policy,
                original, applying, originalRows.Persist, move, false, token);
            await Assert.That(rows.Intent).IsEqualTo(originalRows.Intent);
            await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state, id, request, completed, token);
            await TenantCapacityPlacementRf3FailureDomainsAuthority.PersistReplayAsync(seed,
                original.PersistId, original.Persist, original.Pending, token);
        }
        await TenantCapacityPlacementRf3FailureDomainsAuthority.PersistReplayAsync(seed, persistId, persist, pending, token);
        await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
    }
}
