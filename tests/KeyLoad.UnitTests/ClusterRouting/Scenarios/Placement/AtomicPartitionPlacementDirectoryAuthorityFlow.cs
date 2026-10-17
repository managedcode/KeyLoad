using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementDirectoryAuthorityFlow
{
    private const int MalformedVersion = 2;
    private const int HeaderPointReads = 2;
    private static readonly Guid ForeignOwner = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");

    internal static async Task RunAsync(PhysicalShardCatalogFixture fixture,
        AtomicPartitionPlacementV1 row, ReplicatedOperation original)
    {
        var directory = fixture.Store.Read(view => view.GetRecord<AtomicPartitionPlacementDirectoryV1>(
            AtomicPartitionPlacementTestSupport.DirectoryKey()))!;
        Replace(fixture, directory with
        {
            Version = MalformedVersion
        }, row with
        {
            PhysicalShardId = ForeignOwner
        });
        var image = AtomicPartitionPlacementAuthorityAssertions.Capture(fixture);
        var cut = fixture.Store.Position;
        await RefuseAsync(fixture, original, image, cut);
        fixture.Reopen();
        await RefuseAsync(fixture, original, image, cut);
        Replace(fixture, directory, row);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture,
            row.Partition, directory.Revision);
        var result = fixture.Database.Apply(original);
        await Assert.That(result.Get<bool>()).IsTrue();
        var receipt = NativeSerialization.Serialize(result);
        var repairedImage = AtomicPartitionPlacementAuthorityAssertions.Capture(fixture);
        var repairedCut = fixture.Store.Position;
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(original))
            .SequenceEqual(receipt)).IsTrue();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, repairedImage, repairedCut);
        fixture.Reopen();
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(original))
            .SequenceEqual(receipt)).IsTrue();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, repairedImage, repairedCut);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture,
            row.Partition, directory.Revision);
    }

    private static async Task RefuseAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, byte[] image, long cut)
    {
        var bytes = HeaderBytes(fixture);
        var grant = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits))
            .CreateReadGrant(bytes, HeaderPointReads);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            AtomicPartitionPlacementAuthorityAssertions.Query(fixture, grant));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(HeaderPointReads);
        await Assert.That(grant.ReadBytes).IsEqualTo(bytes);
        var apply = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(original));
        await Assert.That(apply.Code).IsEqualTo(ErrorCode.Corruption);
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static long HeaderBytes(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view =>
        {
            byte[][] keys =
            [
                PhysicalShardCatalogRecordSerialization.CatalogKey(),
                AtomicPartitionPlacementTestSupport.DirectoryKey()
            ];
            return keys.Sum(key => key.LongLength + (view.ReadOwnedValue(key)?.LongLength ?? 0));
        });

    private static void Replace(PhysicalShardCatalogFixture fixture,
        AtomicPartitionPlacementDirectoryV1 directory, AtomicPartitionPlacementV1 row)
        => fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.DirectoryKey(), directory);
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.RowKey(row.Partition), row);
            return true;
        });
}
