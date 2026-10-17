using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementNativeContractTests
{
    [Test]
    public async Task AcPmap001GeneratedContractsRoundTripFullPartitionAndRevisions()
    {
        var request = new BindAtomicPartitionPlacementRequest(1, 0,
            AtomicPartitionPlacementTestSupport.First, AtomicPartitionPlacementTestSupport.ShardId);
        var voters = ImmutableArray.Create("node-a", "node-b", "node-c");
        var row = new AtomicPartitionPlacementV1(1, request.Partition, request.PhysicalShardId, 1,
            AtomicPartitionPlacementTestSupport.Incarnation, voters, 7);
        var directory = new AtomicPartitionPlacementDirectoryV1(1, 1, 1);
        var readRequest = new AtomicPartitionPlacementReadRequest(1, request.Partition);
        var resolution = new AtomicPartitionPlacementResolution(1, request.Partition, request.PhysicalShardId,
            AtomicPartitionPlacementTestSupport.Incarnation, voters, 7, 1, 1, false);
        var restoredRequest = NativeSerialization.Deserialize<BindAtomicPartitionPlacementRequest>(
            NativeSerialization.Serialize(request));
        var restoredReadRequest = NativeSerialization.Deserialize<AtomicPartitionPlacementReadRequest>(
            NativeSerialization.Serialize(readRequest));
        var restoredRow = NativeSerialization.Deserialize<AtomicPartitionPlacementV1>(
            NativeSerialization.Serialize(row));
        var restoredDirectory = NativeSerialization.Deserialize<AtomicPartitionPlacementDirectoryV1>(
            NativeSerialization.Serialize(directory));
        var restoredResolution = NativeSerialization.Deserialize<AtomicPartitionPlacementResolution>(
            NativeSerialization.Serialize(resolution));

        await Assert.That(restoredRequest).IsEqualTo(request);
        await Assert.That(restoredReadRequest).IsEqualTo(readRequest);
        await Assert.That(restoredRow.Version).IsEqualTo(row.Version);
        await Assert.That(restoredRow.Partition).IsEqualTo(row.Partition);
        await Assert.That(restoredRow.PhysicalShardId).IsEqualTo(row.PhysicalShardId);
        await Assert.That(restoredRow.Revision).IsEqualTo(row.Revision);
        await Assert.That(restoredRow.Incarnation).IsEqualTo(row.Incarnation);
        await Assert.That(restoredRow.VoterIds.SequenceEqual(voters, StringComparer.Ordinal)).IsTrue();
        await Assert.That(restoredRow.PlacementEpoch).IsEqualTo(row.PlacementEpoch);
        await Assert.That(restoredDirectory).IsEqualTo(directory);
        await Assert.That(restoredResolution.Version).IsEqualTo(resolution.Version);
        await Assert.That(restoredResolution.Partition).IsEqualTo(resolution.Partition);
        await Assert.That(restoredResolution.PhysicalShardId).IsEqualTo(resolution.PhysicalShardId);
        await Assert.That(restoredResolution.Incarnation).IsEqualTo(resolution.Incarnation);
        await Assert.That(restoredResolution.VoterIds.SequenceEqual(resolution.VoterIds, StringComparer.Ordinal)).IsTrue();
        await Assert.That(restoredResolution.PlacementEpoch).IsEqualTo(resolution.PlacementEpoch);
        await Assert.That(restoredResolution.DirectoryRevision).IsEqualTo(resolution.DirectoryRevision);
        await Assert.That(restoredResolution.Revision).IsEqualTo(resolution.Revision);
        await Assert.That(restoredResolution.IsFallback).IsEqualTo(resolution.IsFallback);
    }
}
