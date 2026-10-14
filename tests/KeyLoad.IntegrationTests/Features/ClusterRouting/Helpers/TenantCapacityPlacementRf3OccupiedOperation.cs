using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedOperation
{
    private const long OccupiedDirectoryRevision = 4;
    private const long RepairedDirectoryRevision = 5;
    private const long CompletedDirectoryRevision = 6;
    private const long InitialIntentRevision = 0;
    private const long ControlBindingRevisionStep = 1;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, CancellationToken token)
    {
        await TenantCapacityPlacementRf3OccupiedAssertions.RefuseAsync(seed, capacity, token);
        var initial = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireDirtyReplayAsync(wave, seed, capacity, initial, token);
        var packed = await TenantCapacityPlacementRf3OccupiedProvisioning.CreateAsync(wave, seed, capacity, token);
        await TenantDedicatedOwnerRf3Assertions.DeniedAsync(wave, packed, token);
        await TenantDedicatedOwnerRf3Assertions.SiblingAsync(wave, packed,
            packed.SiblingPlacement.DirectoryRevision + ControlBindingRevisionStep, token);
        var forward = packed.DeniedMove;
        var occupied = await TenantCapacityPlacementRf3OccupiedMoves.RunAsync(wave, forward, token);
        var occupiedPlacement = TenantCapacityPlacementRf3OccupiedPlacement.Published(
            occupied, packed.Sibling.Partition, OccupiedDirectoryRevision);
        await TenantCapacityPlacementRf3OccupiedAssertions.SiblingAsync(wave, packed, occupiedPlacement, token);
        await TenantCapacityPlacementRf3OccupiedPlacement.RequireAsync(
            seed, capacity, packed, occupiedPlacement, OccupiedDirectoryRevision, token);
        await TenantCapacityPlacementRf3OccupiedAssertions.RefuseAsync(seed, capacity, token);
        await TenantCapacityPlacementRf3OccupiedCold.RequireAsync(wave, seed, capacity, packed,
            forward, occupied, occupiedPlacement, OccupiedDirectoryRevision, token);
        var reverse = new PartitionMoveRequest(Guid.NewGuid(), packed.Sibling.Partition,
            seed.Directory.ControlOwner.PhysicalShardId, OccupiedDirectoryRevision, PartitionMoveMode.Transfer);
        var repaired = await TenantCapacityPlacementRf3OccupiedMoves.RunAsync(wave, reverse, token);
        var repairedPlacement = TenantCapacityPlacementRf3OccupiedPlacement.Published(
            repaired, packed.Sibling.Partition, RepairedDirectoryRevision);
        await TenantCapacityPlacementRf3OccupiedAssertions.SiblingAsync(wave, packed, repairedPlacement, token);
        await TenantCapacityPlacementRf3OccupiedPlacement.RequireAsync(
            seed, capacity, packed, repairedPlacement, RepairedDirectoryRevision, token);
        await TenantCapacityPlacementRf3OccupiedMoves.ReplayAsync(wave, forward, occupied, token);
        var plan = await TenantCapacityPlacementRf3OccupiedPlan.ReadAsync(seed, capacity, token);
        var id = Guid.NewGuid();
        var request = new PersistPlacementIntentRequest(Guid.NewGuid(), InitialIntentRevision, plan.SignedPlan, capacity.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(id, request, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(pending, plan.Plan, request.IntentId);
        await TenantCapacityPlacementRf3ExpiryAssertions.PersistReplayAsync(seed, id, request, pending, token);
        await TenantCapacityPlacementRf3Apply.RunAsync(wave, seed, capacity, plan, pending, token);
        await TenantCapacityPlacementRf3OccupiedMoves.ReplayAsync(wave, forward, occupied, token);
        await TenantCapacityPlacementRf3OccupiedMoves.ReplayAsync(wave, reverse, repaired, token);
        await TenantCapacityPlacementRf3OccupiedAssertions.SiblingAsync(wave, packed,
            repairedPlacement with
            {
                DirectoryRevision = CompletedDirectoryRevision
            }, token);
        await TenantCapacityPlacementRf3ExpiryAssertions.PersistReplayAsync(seed, id, request, pending, token);
        await TenantDedicatedOwnerRf3Assertions.DeniedAsync(wave, packed, token);
        var healthy = await TenantCapacityPlacementRf3OccupiedHealthy.CreateAsync(wave, seed, packed, token);
        await TenantCapacityPlacementRf3OccupiedHealthyCold.RequireAsync(wave, seed, capacity, packed,
            reverse, repaired, occupied, repairedPlacement with
            {
                DirectoryRevision = CompletedDirectoryRevision
            }, healthy, token);
    }
}
