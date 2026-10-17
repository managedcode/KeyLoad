using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PlacementPackingLateOccupancyCurrent
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantDedicatedOwnerRf3State packed,
        AtomicPartitionPlacementResolution sibling, CancellationToken token)
    {
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
        await TenantCapacityPlacementRf3OccupiedAssertions.SiblingAsync(wave, packed, sibling, token);
        await TenantCapacityPlacementRf3OccupiedPlacement.RequireAsync(seed, capacity, packed,
            sibling, sibling.DirectoryRevision, token);
    }

    internal static async Task<PlacementIntentV1> ReadAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3PendingPolicyOperation.Original original, Func<Task> current, CancellationToken token)
    {
        var request = new PlacementIntentReadRequest(new(original.Apply.TenantId, original.Apply.DatabaseId,
            original.Apply.ControlPartition), original.Pending.IntentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(request, token));
        await current();
        await SqlRf3Protocol.EqualAsync(sdk, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.IntentTool, new
            {
                request
            }, token))).Value);
        await current();
        return sdk;
    }
}
