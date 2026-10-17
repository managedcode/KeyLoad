using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PlacementPackingLateOccupancySeed
{
    private const int WireVersion = 1;
    private const long OriginalPrincipalEpoch = 1;
    private const long PendingRevision = 1;
    private const int NoCompletedMoves = 0;
    private const long InitialPersistRevision = 0;
    internal static async Task<TenantCapacityPlacementRf3PendingPolicyOperation.Original> CreateAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity,
        Func<Task> current, CancellationToken token)
    {
        var plan = await PlacementPackingLateOccupancyPlan.ReadAsync(wave, seed, capacity, current, token);
        var persistId = Guid.NewGuid();
        var persist = new PersistPlacementIntentRequest(Guid.NewGuid(), InitialPersistRevision, plan.SignedPlan, capacity.Control);
        var pending = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(persistId, persist, token));
        var expected = new PlacementIntentV1(WireVersion, persist.IntentId,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, OriginalPrincipalEpoch, plan.Plan, PendingRevision, NoCompletedMoves,
            PlacementIntentStatus.Pending, [], null, null, seed.Directory.ControlOwner, null);
        await SqlRf3Protocol.EqualAsync(expected, pending);
        await current();
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = persistId,
                    request = persist
                }, token))).Value);
        await current();
        var apply = new ApplyPlacementIntentRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            pending.IntentId, pending.Revision, capacity.Control);
        return new(pending, persistId, persist, Guid.NewGuid(), apply);
    }
}
