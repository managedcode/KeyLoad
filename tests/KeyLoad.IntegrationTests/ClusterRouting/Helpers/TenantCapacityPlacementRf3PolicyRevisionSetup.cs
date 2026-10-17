using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyRevisionSetup
{
    internal static async Task<TenantPlacementPlanResultV1> RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
    {
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await seed.Source.PlanTenantPlacementAsync(query, token), ErrorCode.ResourceExhausted);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        var dirty = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireDirtyReplayAsync(wave, seed, state, dirty, token);
        await TenantCapacityPlacementRf3MissingMember.RequireAsync(wave, seed, state, query, dirty, token);
        var configure = new ConfigurePlacementPolicyRequest(state.OriginalPolicy.Policy.Revision,
            state.HealthyPolicy, state.Control);
        await SqlRf3Protocol.EqualAsync(state.HealthyPolicy, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigurePlacementPolicyAsync(Guid.NewGuid(), configure, token)));
        var bind = new BindAtomicPartitionPlacementRequest(1, 0, state.Control,
            seed.Directory.ControlOwner.PhysicalShardId);
        var bindId = Guid.NewGuid();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.BindAtomicPartitionPlacementAsync(bindId, bind, token))).IsTrue();
        await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(
            await seed.Official.CallAsync(McpCallerTools.AdminPartitionPlacementBind,
                new
                {
                    commandId = bindId,
                    request = bind
                }, token))).Value).IsTrue();
        var primary = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadAtomicPartitionPlacementAsync(new(1, seed.Partition), token));
        await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement with
        {
            DirectoryRevision = 1
        }, primary);
        var control = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadAtomicPartitionPlacementAsync(new(1, state.Control), token));
        await Assert.That(control.IsFallback).IsFalse();
        await Assert.That(control.Revision).IsEqualTo(1);
        await Assert.That(control.DirectoryRevision).IsEqualTo(1);
        await Assert.That(control.PhysicalShardId).IsEqualTo(seed.Directory.ControlOwner.PhysicalShardId);
        var plan = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        await TenantCapacityPlacementRf3Authorization.RequireAsync(wave, seed, state, plan, token);
        plan = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, plan, token);
        return plan;
    }
}
