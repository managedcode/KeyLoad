using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OwnerLossReplay
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest move,
        TenantCapacityPlacementRf3OwnerLossHeld.Identity identity, PlacementIntentV1 completed, CancellationToken token)
    {
        var publication = checked(identity.Pending.Plan.DirectoryRevision + 1);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed, move, seed.OriginalPlacement,
            seed.Directory.ControlOwner, completed.Receipts.Single(), publication, publication, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, move,
            completed.Receipts.Single(), token);
        await TenantCapacityPlacementRf3Apply.RequireReplayAsync(seed, state, identity.ApplyId, identity.Apply, completed, token);
        await SqlRf3Protocol.EqualAsync(identity.Pending, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(identity.PersistId, identity.Persist, token)));
        await SqlRf3Protocol.EqualAsync(identity.Pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = identity.PersistId,
                    request = identity.Persist
                }, token))).Value);
        await SqlRf3Protocol.EqualAsync(completed, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadPlacementIntentAsync(new(new(identity.Apply.TenantId, identity.Apply.DatabaseId,
                identity.Apply.ControlPartition), identity.Apply.IntentId), token)));
        await TenantCapacityPlacementRf3Assertions.RequirePolicyReplayAsync(seed, state, token);
    }
}
