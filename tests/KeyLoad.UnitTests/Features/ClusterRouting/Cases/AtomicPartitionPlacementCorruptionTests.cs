using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;
namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementCorruptionTests
{
    [Test]
    public async Task AcPmap001WrongFullIdentityAndInvalidDirectoryFailClosed()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.DirectoryKey(),
                new AtomicPartitionPlacementDirectoryV1(1, 1, 1));
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.RowKey(
                AtomicPartitionPlacementTestSupport.First), new AtomicPartitionPlacementV1(1,
                AtomicPartitionPlacementTestSupport.SameSuffixOtherTenant,
                AtomicPartitionPlacementTestSupport.ShardId, 1, AtomicPartitionPlacementTestSupport.Incarnation,
                PhysicalShardCatalogVoterIds.Standard, 1));
            return true;
        });

        var wrongIdentity = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
                AtomicPartitionPlacementTestSupport.Read(AtomicPartitionPlacementTestSupport.First)));
        await Assert.That(wrongIdentity.Code).IsEqualTo(ErrorCode.Corruption);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.DirectoryKey(),
                new AtomicPartitionPlacementDirectoryV1(1, 5000, 5000));
            return true;
        });
        var invalidDirectory = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
                AtomicPartitionPlacementTestSupport.Read(AtomicPartitionPlacementTestSupport.First)));
        await Assert.That(invalidDirectory.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcPmap001OwnerTupleMismatchFailsReadAndBindAsCorruption()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var owner = fixture.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)!.DefaultShard);
        var partition = AtomicPartitionPlacementTestSupport.First;
        var row = new AtomicPartitionPlacementV1(1, partition, owner.PhysicalShardId, 1,
            owner.Incarnation, owner.VoterIds, owner.PlacementEpoch);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.DirectoryKey(),
                new AtomicPartitionPlacementDirectoryV1(1, 1, 1));
            return true;
        });

        await AssertCorruptOwnerTuple(fixture, partition, row with { PhysicalShardId = Guid.NewGuid() });
        await AssertCorruptOwnerTuple(fixture, partition, row with { Incarnation = Guid.NewGuid() });
        await AssertCorruptOwnerTuple(fixture, partition,
            row with { VoterIds = owner.VoterIds.Reverse().ToImmutableArray() });
        await AssertCorruptOwnerTuple(fixture, partition,
            row with { PlacementEpoch = checked(owner.PlacementEpoch + 1) });
    }

    private static async Task AssertCorruptOwnerTuple(PhysicalShardCatalogFixture fixture,
        PartitionRef partition, AtomicPartitionPlacementV1 row)
    {
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.RowKey(partition), row);
            return true;
        });
        var readFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId, AtomicPartitionPlacementTestSupport.Read(partition)));
        await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.Corruption);
        var before = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.RowKey(partition));
        var bound = AtomicPartitionPlacementTestSupport.Bind(fixture,
            new BindAtomicPartitionPlacementRequest(1, 1, partition, AtomicPartitionPlacementTestSupport.ShardId));
        var after = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.RowKey(partition));
        await Assert.That(bound.Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(after.SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task AcPmap001OversizedPersistedValueIsCorruption()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(AtomicPartitionPlacementTestSupport.DirectoryKey(), new byte[8193]);
            return true;
        });

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
                AtomicPartitionPlacementTestSupport.Read(AtomicPartitionPlacementTestSupport.First)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.DirectoryKey(),
                new AtomicPartitionPlacementDirectoryV1(1, 1, 1));
            transaction.Put(AtomicPartitionPlacementTestSupport.RowKey(
                AtomicPartitionPlacementTestSupport.First), new byte[8193]);
            return true;
        });
        var rowFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
                AtomicPartitionPlacementTestSupport.Read(AtomicPartitionPlacementTestSupport.First)));
        await Assert.That(rowFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }
}
