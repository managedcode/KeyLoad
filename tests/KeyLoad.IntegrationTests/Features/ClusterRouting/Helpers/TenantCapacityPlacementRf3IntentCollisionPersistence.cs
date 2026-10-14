using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionPersistence
{
    private const long NewIntentRevision = 0;
    private const long PendingRevision = 1;
    private const long OriginalPrincipalEpoch = 1;
    private const int CompletedMoveCount = 0;
    internal static async Task<TenantPlacementPlanResultV1> PlanAsync(
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
    {
        var request = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request
            }, token))).Value;
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, sdk, token);
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, mcp, token);
        return sdk;
    }

    internal static async Task<PlacementIntentV1> PersistAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid commandId, PersistPlacementIntentRequest request, TenantPlacementPlanResultV1 plan, CancellationToken token)
    {
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(commandId, request, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, request.IntentId);
        var expected = new PlacementIntentV1(TenantPlacementPolicyProtocol.Version, request.IntentId,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, OriginalPrincipalEpoch, plan.Plan,
            PendingRevision, CompletedMoveCount, PlacementIntentStatus.Pending, [], null, null,
            seed.Directory.ControlOwner, null);
        await SqlRf3Protocol.EqualAsync(expected, pending);
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId,
                    request
                }, token))).Value);
        return pending;
    }

    internal static PersistPlacementIntentRequest Request(Guid intentId, TenantPlacementPlanResultV1 plan,
        TenantCapacityPlacementRf3Provisioning.State state)
        => new(intentId, NewIntentRevision, plan.SignedPlan, state.Control);

    internal static PartitionMoveRequest Move(TenantPlacementPlanResultV1 plan)
    {
        var move = plan.Plan.Moves.Single();
        return new(move.MoveId, move.Partition, move.DestinationPhysicalShardId,
            move.ExpectedPlacementRevision, move.Mode);
    }
}
