using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementAuthorityAssertions
{
    private const int MetadataPointReads = 3;
    private const int CurrentVersion = 1;
    private const long InitialPlacementEpoch = 1;
    private const long InitialRowRevision = 1;
    private const string CompleteImageRequired = "The native placement trial must capture its complete bounded canonical image.";

    internal static byte[] Capture(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view =>
        {
            var page = view.Scan([], fixture.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            { throw new InvalidOperationException(CompleteImageRequired); }
            return NativeSerialization.Serialize(page);
        });

    internal static async Task UnchangedAsync(PhysicalShardCatalogFixture fixture, byte[] image, long cut)
    {
        await Assert.That(Capture(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
    }

    internal static ReadExecutionBudgetReadGrant CreateGrant(PhysicalShardCatalogFixture fixture)
        => new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits))
            .CreateReadGrant(ExpectedReadBytes(fixture), MetadataPointReads);

    internal static long ExpectedReadBytes(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view =>
        {
            byte[][] keys =
            [
                PhysicalShardCatalogRecordSerialization.CatalogKey(),
                AtomicPartitionPlacementTestSupport.DirectoryKey(),
                AtomicPartitionPlacementTestSupport.RowKey(AtomicPartitionPlacementTestSupport.First)
            ];
            return keys.Sum(key => key.LongLength + (view.ReadOwnedValue(key)?.LongLength ?? 0));
        });

    private const int PublishedLineagePointRead = 1;

    internal static ReadExecutionBudgetReadGrant CreateLineageGrant(PhysicalShardCatalogFixture fixture)
        => new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits))
            .CreateReadGrant(ExpectedLineageReadBytes(fixture), MetadataPointReads + PublishedLineagePointRead);

    internal static byte[]? LineageBytes(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view => view.ReadOwnedValue(LineageKey()));

    internal static long ExpectedLineageReadBytes(PhysicalShardCatalogFixture fixture)
        => ExpectedReadBytes(fixture) + LineageKey().LongLength + (LineageBytes(fixture)?.LongLength ?? 0);

    private static byte[] LineageKey()
        => KeyLoad.Core.Features.ClusterRouting.Serialization.PartitionMovePublishedPlacementStorage.Key(
            AtomicPartitionPlacementTestSupport.First);

    internal static AtomicPartitionPlacementResolution Query(PhysicalShardCatalogFixture fixture,
        ReadExecutionBudgetReadGrant grant)
        => fixture.Database.WithQueryView(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.First, AtomicPartitionPlacementAuthorityFlow.Collection,
            (view, _, _) => DatabaseEngine.ReadAtomicPartitionPlacementForAuthorizedQuery(view,
                AtomicPartitionPlacementTestSupport.First, grant));

    internal static async Task WitnessAsync(PhysicalShardCatalogFixture fixture,
        PartitionRef partition, long directoryRevision)
    {
        var actual = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(partition));
        var expected = new AtomicPartitionPlacementResolution(CurrentVersion, partition,
            AtomicPartitionPlacementTestSupport.ShardId, AtomicPartitionPlacementTestSupport.Incarnation,
            PhysicalShardCatalogVoterIds.Standard, InitialPlacementEpoch, directoryRevision, InitialRowRevision, false);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(
            NativeSerialization.Serialize(expected))).IsTrue();
        var directory = fixture.Store.Read(view => view.GetRecord<AtomicPartitionPlacementDirectoryV1>(
            AtomicPartitionPlacementTestSupport.DirectoryKey()))!;
        await Assert.That(directory.Version).IsEqualTo(CurrentVersion);
        await Assert.That(directory.Revision).IsEqualTo(directoryRevision);
        await Assert.That(directory.ExplicitAssignmentCount).IsEqualTo(checked((int)directoryRevision));
    }
}
