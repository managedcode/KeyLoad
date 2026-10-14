using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3SecondOwnerLossHeld
{
    private const int First = 0;
    private const int CompletedFirst = 1;

    internal static async Task<TenantCapacityPlacementRf3OwnerLossHeld.State> CaptureAsync(
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMoveRequest[] moves, PlacementIntentV1 pending, Guid id, ApplyPlacementIntentRequest request,
        RequestCqrsProbeFixture controls, ReplicaSiloDiscovery[] discovery,
        Task<Result<PlacementIntentV1>> original, Task<RequestCqrsProbeMarkerRecord> markerTask, CancellationToken token)
    {
        if (await Task.WhenAny(original, markerTask) == original)
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await original);
            throw new InvalidOperationException("The original Apply finished before its second committed Prepare owner-loss cut.");
        }
        var marker = await markerTask;
        var journal = RequestCqrsMovementJournalReader.Read(controls, marker);
        var applying = await TenantCapacityPlacementRf3MultiHeldAssertions.ReadAsync(seeds[First], request, token);
        await TenantCapacityPlacementRf3MultiHeldAssertions.RequireAsync(
            pending, applying, moves, id, request, marker, journal, discovery);
        await TenantCapacityPlacementRf3MultiAssertions.PlacementsAsync(seeds[First], state, moves, CompletedFirst, token);
        var first = seeds.Single(seed => seed.Partition == moves[First].Partition);
        await PartitionMovementPublicCallerReplay.RequireAsync(first.Source, first.Official,
            moves[First], applying.Receipts[First], token);
        await Assert.That(original.IsCompleted).IsFalse();
        await SqlRf3Protocol.EqualAsync(applying,
            await TenantCapacityPlacementRf3MultiHeldAssertions.ReadAsync(seeds[First], request, token));
        await SqlRf3Protocol.EqualAsync(journal, RequestCqrsMovementJournalReader.Read(controls, marker));
        await Assert.That(original.IsCompleted).IsFalse();
        return new(applying, marker, journal, discovery);
    }
}
