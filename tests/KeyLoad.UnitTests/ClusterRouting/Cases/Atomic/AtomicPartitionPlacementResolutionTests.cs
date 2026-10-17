using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementResolutionTests
{
    [Test]
    public async Task AcPmap001MissingRowsReturnCommittedDefaultWithoutWriting()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var before = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());

        var fallback = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(AtomicPartitionPlacementTestSupport.First));
        var after = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());

        await Assert.That(fallback.IsFallback).IsTrue();
        await Assert.That(fallback.DirectoryRevision).IsEqualTo(0);
        await Assert.That(fallback.Revision).IsEqualTo(0);
        await Assert.That(fallback.PhysicalShardId).IsEqualTo(AtomicPartitionPlacementTestSupport.ShardId);
        await Assert.That(fallback.Incarnation).IsEqualTo(AtomicPartitionPlacementTestSupport.Incarnation);
        await Assert.That(before.Length).IsEqualTo(0);
        await Assert.That(after.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AcPmap003FallbackCarriesCurrentDirectoryRevisionWithoutCreatingARow()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var bound = new BindAtomicPartitionPlacementRequest(1, 0,
            AtomicPartitionPlacementTestSupport.First, AtomicPartitionPlacementTestSupport.ShardId);
        await Assert.That(AtomicPartitionPlacementTestSupport.Bind(fixture, bound).Error).IsNull();

        var fallback = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(AtomicPartitionPlacementTestSupport.SameSuffixOtherTenant));
        var rowBytes = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.RowKey(AtomicPartitionPlacementTestSupport.SameSuffixOtherTenant));

        await Assert.That(fallback.IsFallback).IsTrue();
        await Assert.That(fallback.DirectoryRevision).IsEqualTo(1);
        await Assert.That(fallback.Revision).IsEqualTo(0);
        await Assert.That(rowBytes.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AcPmap001RowsUseAllFourPartitionComponentsAndSurviveReopen()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var firstRequest = Request(0, AtomicPartitionPlacementTestSupport.First);
        var secondRequest = Request(1, AtomicPartitionPlacementTestSupport.SameSuffixOtherTenant);

        await Assert.That(AtomicPartitionPlacementTestSupport.Bind(fixture, firstRequest).Error).IsNull();
        await Assert.That(AtomicPartitionPlacementTestSupport.Bind(fixture, secondRequest).Error).IsNull();
        var firstKey = AtomicPartitionPlacementTestSupport.RowKey(firstRequest.Partition);
        var secondKey = AtomicPartitionPlacementTestSupport.RowKey(secondRequest.Partition);
        await Assert.That(firstKey.SequenceEqual(secondKey)).IsFalse();
        fixture.Reopen();

        var first = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(firstRequest.Partition));
        var second = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(secondRequest.Partition));
        var directory = fixture.Store.Read(view => view.GetRecord<AtomicPartitionPlacementDirectoryV1>(
            AtomicPartitionPlacementTestSupport.DirectoryKey()));
        await Assert.That(first.IsFallback).IsFalse();
        await Assert.That(first.DirectoryRevision).IsEqualTo(2);
        await Assert.That(first.Revision).IsEqualTo(1);
        await Assert.That(second.IsFallback).IsFalse();
        await Assert.That(second.DirectoryRevision).IsEqualTo(2);
        await Assert.That(second.Revision).IsEqualTo(1);
        await Assert.That(first.Incarnation).IsEqualTo(AtomicPartitionPlacementTestSupport.Incarnation);
        await Assert.That(first.VoterIds.SequenceEqual(PhysicalShardCatalogVoterIds.Standard, StringComparer.Ordinal)).IsTrue();
        await Assert.That(first.PlacementEpoch).IsEqualTo(1);
        await Assert.That(directory!.Revision).IsEqualTo(2);
        await Assert.That(directory.ExplicitAssignmentCount).IsEqualTo(2);
    }

    private static BindAtomicPartitionPlacementRequest Request(long revision, PartitionRef partition)
        => new(1, revision, partition, AtomicPartitionPlacementTestSupport.ShardId);
}
