using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3Placement
{
    private const int Version = 1;
    private const long EpochIncrement = 1;
    private const long EmptyPosition = 0;
    private const string MissingReceipt = "Actual installed native receipt is absent.";

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest request,
        AtomicPartitionPlacementResolution original, PhysicalShardRecord source, PartitionMoveResult actual,
        long publicationRevision, long currentDirectoryRevision, CancellationToken token)
    {
        var target = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
            == request.DestinationPhysicalShardId).Owner;
        await Assert.That(target.PlacementEpoch).IsLessThanOrEqualTo(original.PlacementEpoch);
        var epoch = checked(original.PlacementEpoch + EpochIncrement);
        var receipt = actual.InstalledReceipt ?? throw new InvalidOperationException(MissingReceipt);
        await Assert.That(receipt.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(receipt.Incarnation).IsEqualTo(target.Incarnation);
        await Assert.That(receipt.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.OwnershipEpoch).IsEqualTo(epoch);
        await Assert.That(actual.SourceCut).IsGreaterThan(EmptyPosition);
        var placement = new AtomicPartitionPlacementV1(Version, seed.Partition, target.PhysicalShardId,
            publicationRevision, target.Incarnation, target.VoterIds, epoch);
        await SqlRf3Protocol.EqualAsync(new PartitionMoveResult(request.MoveId, seed.Partition,
            PartitionMovePhase.Retired, source, target, actual.SourceCut, receipt, placement), actual);
        var current = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(Version, seed.Partition), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(Version, seed.Partition,
            target.PhysicalShardId, target.Incarnation, target.VoterIds, epoch,
            currentDirectoryRevision, publicationRevision, false), current);
    }
}
