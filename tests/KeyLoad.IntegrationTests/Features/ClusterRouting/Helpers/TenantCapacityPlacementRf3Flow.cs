using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
        => await RunAsync(wave, seed, state, committedOwnerLoss: false, token).ConfigureAwait(false);

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        bool committedOwnerLoss, CancellationToken token)
        => await RunAsync(wave, seed, state, committedOwnerLoss, originalPlanExpiry: false, token).ConfigureAwait(false);

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        bool committedOwnerLoss, bool originalPlanExpiry, CancellationToken token)
        => await RunAsync(wave, seed, state, committedOwnerLoss, originalPlanExpiry, pendingIntentRevocation: false, token).ConfigureAwait(false);

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        bool committedOwnerLoss, bool originalPlanExpiry, bool pendingIntentRevocation, CancellationToken token)
    {
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(await seed.Source.PlanTenantPlacementAsync(query, token),
            ErrorCode.ResourceExhausted);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        var dirty = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireDirtyReplayAsync(wave, seed, state, dirty, token);
        await TenantCapacityPlacementRf3MissingMember.RequireAsync(wave, seed, state, query, dirty, token);
        var healthy = new ConfigurePlacementPolicyRequest(state.OriginalPolicy.Policy.Revision,
            state.HealthyPolicy, state.Control);
        _ = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigurePlacementPolicyAsync(
            Guid.NewGuid(), healthy, token));
        var binding = await RequireRevisionSeparationAsync(seed, state, token);
        var plan = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        await TenantCapacityPlacementRf3Authorization.RequireAsync(wave, seed, state, plan, pendingIntentRevocation, token);
        if (pendingIntentRevocation)
        {
            await RequireBindReplayAsync(seed, binding.Id, binding.Request, token);
            return;
        }
        plan = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        var intentId = Guid.NewGuid();
        var persistId = Guid.NewGuid();
        if (originalPlanExpiry)
        { plan = await TenantCapacityPlacementRf3Expiry.RequireAsync(wave, seed, state, persistId, intentId, token); }
        var persist = new PersistPlacementIntentRequest(intentId, 0, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, intentId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new { commandId = persistId, request = persist }, token))).Value);
        if (committedOwnerLoss)
        { await TenantCapacityPlacementRf3OwnerLoss.RunAsync(wave, seed, state, plan, pending, persistId, persist, token); }
        else
        { await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, state, plan, pending, token); }
        await RequireBindReplayAsync(seed, binding.Id, binding.Request, token);
        if (originalPlanExpiry)
        { await TenantCapacityPlacementRf3ExpiryAssertions.PersistReplayAsync(seed, persistId, persist, pending, token); }
    }

    private static async Task<(Guid Id, BindAtomicPartitionPlacementRequest Request)> RequireRevisionSeparationAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        await Assert.That(seed.OriginalPlacement.IsFallback).IsTrue();
        await Assert.That(seed.OriginalPlacement.Revision).IsEqualTo(0);
        await Assert.That(seed.OriginalPlacement.DirectoryRevision).IsEqualTo(0);
        var id = Guid.NewGuid();
        var request = new BindAtomicPartitionPlacementRequest(1, 0, state.Control,
            seed.Directory.ControlOwner.PhysicalShardId);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.BindAtomicPartitionPlacementAsync(id, request, token))).IsTrue();
        await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(
            await seed.Official.CallAsync(McpCallerTools.AdminPartitionPlacementBind,
                new { commandId = id, request }, token))).Value).IsTrue();
        var current = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(1, seed.Partition), token));
        await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement with { DirectoryRevision = 1 }, current);
        var control = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(1, state.Control), token));
        await Assert.That(control.IsFallback).IsFalse();
        await Assert.That(control.Revision).IsEqualTo(1);
        await Assert.That(control.DirectoryRevision).IsEqualTo(1);
        await Assert.That(control.PhysicalShardId).IsEqualTo(seed.Directory.ControlOwner.PhysicalShardId);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        return (id, request);
    }

    private static async Task RequireBindReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid id, BindAtomicPartitionPlacementRequest request, CancellationToken token)
    {
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.BindAtomicPartitionPlacementAsync(id, request, token))).IsTrue();
        await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(
            await seed.Official.CallAsync(McpCallerTools.AdminPartitionPlacementBind,
                new { commandId = id, request }, token))).Value).IsTrue();
    }
}
