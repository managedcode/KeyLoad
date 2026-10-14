using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3RevocationApply
{
    private const int FormatVersion = 1;
    private const long EmptyRevision = 0;
    private const long RepairedPolicyEpoch = 4;
    private const long PublicationRevision = 2;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantCapacityPlacementRf3RevocationCaller caller,
        PlacementIntentV1 oldIntent, Guid oldId, ApplyPlacementIntentRequest oldRequest,
        Guid oldPersistId, PersistPlacementIntentRequest oldPersist, TenantCapacityPlacementRf3RevocationCold.Rows oldRow, CancellationToken token)
    {
        var source = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(FormatVersion, seed.Partition), token));
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var officialPlan = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request = query
            }, token))).Value;
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, officialPlan, token);
        var plan = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), EmptyRevision, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, persist.IntentId);
        await Assert.That(pending.IntentId).IsNotEqualTo(oldIntent.IntentId);
        await Assert.That(pending.PolicyEpoch).IsEqualTo(RepairedPolicyEpoch);
        await Assert.That(pending.PrincipalId).IsEqualTo(oldIntent.PrincipalId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = persistId,
                    request = persist
                }, token))).Value);
        await TenantCapacityPlacementRf3ReadAuthorityAssertions.CurrentAsync(caller,
            new(seed.Partition.TenantId, seed.Partition.DatabaseId, state.Control), state.HealthyPolicy, pending, token);
        var id = Guid.NewGuid();
        var request = new ApplyPlacementIntentRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            pending.IntentId, pending.Revision, state.Control);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ApplyPlacementIntentAsync(id, request, token));
        await TenantCapacityPlacementRf3RevocationAssertions.CompletedAsync(pending, completed, id, request);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed, plan.Plan.Moves.Single(), source,
            seed.Directory.ControlOwner, completed.Receipts.Single(), PublicationRevision, PublicationRevision, token);
        await TenantCapacityPlacementRf3RevocationReplay.RequireAsync(seed, state, caller, completed,
            id, request, pending, persistId, persist, oldIntent, oldId, oldRequest, oldPersistId, oldPersist, token);
        await TenantCapacityPlacementRf3RevocationHealthyCold.RequireAsync(wave, seed, state, caller,
            completed, id, request, oldIntent, oldPersistId, oldRow, token);
        await TenantCapacityPlacementRf3ReadAuthorityAssertions.CurrentAsync(caller,
            new(seed.Partition.TenantId, seed.Partition.DatabaseId, state.Control), state.HealthyPolicy, completed, token);
        await TenantCapacityPlacementRf3CompletedPolicyReplay.RunAsync(wave, seed, state, caller,
            new(pending, persistId, persist, completed, id, request, oldIntent, oldPersistId, oldRow), token);
        await TenantCapacityPlacementRf3RevocationReplay.RequireAsync(seed, state, caller, completed,
            id, request, pending, persistId, persist, oldIntent, oldId, oldRequest, oldPersistId, oldPersist, token);
        await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
    }
}
