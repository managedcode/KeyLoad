using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3MoveTerminal
{
    private const int Version = 1;
    private const long NextRevision = 1L;
    private const long EmptyPosition = 0L;
    private const string MissingReceipt = "The actual moved inventory installation receipt is absent.";

    internal static async Task VerifyAsync(RequestCqrsRf3Callers callers,
        PhysicalOwnerDirectoryV1 directory, AtomicPartitionPlacementResolution before,
        PartitionMoveRequest request, PartitionMoveResult actual, CancellationToken token)
    {
        var source = directory.Owners.Single(owner => owner.Owner.PhysicalShardId == before.PhysicalShardId).Owner;
        var target = directory.Owners.Single(owner => owner.Owner.PhysicalShardId == request.DestinationPhysicalShardId).Owner;
        await Assert.That(target.PhysicalShardId).IsNotEqualTo(source.PhysicalShardId);
        var receipt = actual.InstalledReceipt ?? throw new InvalidOperationException(MissingReceipt);
        await Assert.That(receipt.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(receipt.Incarnation).IsEqualTo(target.Incarnation);
        await Assert.That(receipt.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(receipt.OwnershipEpoch).IsEqualTo(target.PlacementEpoch);
        await Assert.That(actual.SourceCut).IsGreaterThan(EmptyPosition);
        var epoch = checked(Math.Max(before.PlacementEpoch, target.PlacementEpoch) + NextRevision);
        var revision = checked(before.DirectoryRevision + NextRevision);
        var placement = new AtomicPartitionPlacementV1(Version, request.Partition, target.PhysicalShardId,
            revision, target.Incarnation, target.VoterIds, epoch);
        await SqlRf3Protocol.EqualAsync(new PartitionMoveResult(request.MoveId, request.Partition,
            PartitionMovePhase.Retired, source, target, actual.SourceCut, receipt, placement), actual);
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(Version, request.Partition,
            target.PhysicalShardId, target.Incarnation, target.VoterIds, epoch, revision, revision, false),
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(
                new(Version, request.Partition), token)));
    }
}
