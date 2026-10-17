using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PlacementPackingLateOccupancyStages
{
    private const long OriginalDirectoryRevision = 3;
    private const long FirstApplyingRevision = 2;
    private const long RenewedApplyingRevision = 3;
    internal static Task<TenantCapacityPlacementRf3PendingPolicyOperation.Original> SeedAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantDedicatedOwnerRf3State packed,
        CancellationToken token)
        => PlacementPackingLateOccupancySeed.CreateAsync(wave, seed, capacity,
            () => PlacementPackingLateOccupancyCurrent.RequireAsync(wave, seed, capacity, packed,
                packed.SiblingPlacement with
                {
                    DirectoryRevision = OriginalDirectoryRevision
                }, token), token);

    internal static async Task<PlacementPackingLateOccupancyCold> OccupiedAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantDedicatedOwnerRf3State packed, TenantCapacityPlacementRf3PendingPolicyOperation.Original original,
        AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        var applying = await PlacementPackingLateOccupancyRefusal.RunAsync(wave, seed, original, FirstApplyingRevision,
            () => PlacementPackingLateOccupancyCurrent.RequireAsync(wave, seed, capacity, packed, placement, token), token);
        return new(original, applying, capacity.HealthyPolicy);
    }

    internal static async Task RepairedAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantDedicatedOwnerRf3State packed, TenantCapacityPlacementRf3PendingPolicyOperation.Original original,
        AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        _ = await PlacementPackingLateOccupancyRefusal.RunAsync(wave, seed, original, RenewedApplyingRevision,
            () => PlacementPackingLateOccupancyCurrent.RequireAsync(wave, seed, capacity, packed, placement, token), token);
        await SqlRf3Protocol.EqualAsync(original.Pending, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(original.PersistId, original.Persist, token)));
        await PlacementPackingLateOccupancyCurrent.RequireAsync(wave, seed, capacity, packed, placement, token);
        await SqlRf3Protocol.EqualAsync(original.Pending,
            (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = original.PersistId,
                    request = original.Persist
                }, token))).Value);
        await PlacementPackingLateOccupancyCurrent.RequireAsync(wave, seed, capacity, packed, placement, token);
    }
}
