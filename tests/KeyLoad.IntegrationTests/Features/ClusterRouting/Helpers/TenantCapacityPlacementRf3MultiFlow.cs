using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiFlow
{
    private const int FirstSeed = 0;
    private const long NewIntentRevision = 0;
    internal static Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
        => RunAsync(wave, seeds, state, committedSecondOwnerLoss: false, token);

    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        bool committedSecondOwnerLoss, CancellationToken token)
    {
        var first = seeds[FirstSeed];
        var query = new TenantPlacementPlanRequest(first.Partition.TenantId,
            first.Partition.DatabaseId, [.. seeds.Select(seed => seed.Partition)], state.Control);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await first.Source.PlanTenantPlacementAsync(query, token), ErrorCode.ResourceExhausted);
        await TenantCapacityPlacementRf3MultiCold.RunAsync(wave, seeds, state,
            seeds.Select(seed => seed.FirstRequest).ToArray(), null, null, null, token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await first.Source.ConfigurePlacementPolicyAsync(
            Guid.NewGuid(), new(state.OriginalPolicy.Policy.Revision, state.HealthyPolicy, state.Control), token));
        var binding = await TenantCapacityPlacementRf3MultiAssertions.BindControlAsync(first, state, token);
        await TenantCapacityPlacementRf3CompleteRosterAssertions.RequireAsync(wave, seeds, state, token);
        var plan = await McpCallerAssertions.SdkSuccessAsync(await first.Source.PlanTenantPlacementAsync(query, token));
        await TenantCapacityPlacementRf3MultiAssertions.PlanAsync(seeds, state, plan);
        var moves = plan.Plan.Moves.ToArray();
        var intentId = Guid.NewGuid();
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(intentId, NewIntentRevision, plan.SignedPlan, state.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await first.Source.PersistPlacementIntentAsync(persistId, persist, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, intentId);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await first.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = persistId,
                    request = persist
                }, token))).Value);
        var id = Guid.NewGuid();
        var request = new ApplyPlacementIntentRequest(first.Partition.TenantId,
            first.Partition.DatabaseId, intentId, pending.Revision, state.Control);
        var completed = committedSecondOwnerLoss
            ? await TenantCapacityPlacementRf3SecondOwnerLoss.RunAsync(wave, seeds,
                state, moves, pending, id, request, token)
            : await TenantCapacityPlacementRf3MultiHold.RunAsync(wave, seeds,
                state, moves, pending, id, request, token);
        await TenantCapacityPlacementRf3MultiReplay.RequireAsync(seeds, state, moves,
            id, request, completed, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await first.Source.ApplyPlacementIntentAsync(id,
                request with
                {
                    ExpectedRevision = completed.Revision
                }, token), ErrorCode.Conflict);
        await TenantCapacityPlacementRf3MultiReplay.RequireAsync(seeds, state, moves,
            id, request, completed, token);
        await TenantCapacityPlacementRf3MultiCold.RunAsync(wave, seeds, state,
            moves, id, request, completed, token);
        await TenantCapacityPlacementRf3MultiAssertions.ReplayBindingAsync(first, binding.Id, binding.Request, token);
        await TenantCapacityPlacementRf3MultiReplay.RequireAsync(seeds, state, moves,
            id, request, completed, token);
        foreach (var seed in seeds)
        {
            await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
        }
    }
}
