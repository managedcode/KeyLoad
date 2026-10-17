using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedPlacement
{
    private const int CurrentVersion = 1;
    private const long ControlRowRevision = 1;
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantDedicatedOwnerRf3State packed,
        AtomicPartitionPlacementResolution sibling, long directoryRevision, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(packed.Original with
        {
            DirectoryRevision = directoryRevision
        },
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
                new(CurrentVersion, seed.Partition), token)));
        var owner = seed.Directory.ControlOwner;
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(CurrentVersion, capacity.Control,
            owner.PhysicalShardId, owner.Incarnation, owner.VoterIds, owner.PlacementEpoch,
            directoryRevision, ControlRowRevision, false), await McpCallerAssertions.SdkSuccessAsync(
                await seed.Source.ReadAtomicPartitionPlacementAsync(new(CurrentVersion, capacity.Control), token)));
        await Assert.That(sibling.DirectoryRevision).IsEqualTo(directoryRevision);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
    }

    internal static AtomicPartitionPlacementResolution Published(PartitionMoveResult receipt,
        PartitionRef partition, long expectedRevision)
        => new(CurrentVersion, partition, receipt.DestinationOwner.PhysicalShardId, receipt.DestinationOwner.Incarnation,
            receipt.DestinationOwner.VoterIds, receipt.PublishedPlacement!.PlacementEpoch,
            expectedRevision, expectedRevision, false);
}
