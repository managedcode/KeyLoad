using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementQueryViewTests
{
    [Test]
    public async Task AcPquery003OrdinaryAuthorizedReaderResolvesFullOwnerAndChargesExactThreePointReads()
    {
        using var fixture = new AtomicPartitionPlacementQueryViewFixture();
        var exactBytes = fixture.ExpectedReadBytes();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits));
        var grant = budget.CreateReadGrant(exactBytes, 3);
        var resolution = fixture.Query(grant);

        await Assert.That(fixture.ReaderIsClusterAdministrator).IsFalse();
        await Assert.That(resolution.Version).IsEqualTo(1);
        await Assert.That(resolution.Partition).IsEqualTo(AtomicPartitionPlacementQueryViewFixture.Partition);
        await Assert.That(resolution.PhysicalShardId).IsEqualTo(AtomicPartitionPlacementQueryViewFixture.ShardId);
        await Assert.That(resolution.Incarnation).IsEqualTo(AtomicPartitionPlacementQueryViewFixture.Incarnation);
        await Assert.That(resolution.VoterIds.SequenceEqual(AtomicPartitionPlacementQueryViewFixture.Voters)).IsTrue();
        await Assert.That(resolution.PlacementEpoch).IsEqualTo(1L);
        await Assert.That(resolution.DirectoryRevision).IsEqualTo(1L);
        await Assert.That(resolution.Revision).IsEqualTo(1L);
        await Assert.That(resolution.IsFallback).IsFalse();
        await Assert.That(grant.ExaminedRecords).IsEqualTo(3);
        await Assert.That(grant.ReadBytes).IsEqualTo(exactBytes);
        await Assert.That(budget.ReadBytes).IsEqualTo(exactBytes);
    }

    [Test]
    public async Task AcPquery003DirectoryAndRowMissesAreChargedAndResolveSameViewFallback()
    {
        using var fixture = new AtomicPartitionPlacementQueryViewFixture(bindPartition: false);
        var exactBytes = fixture.ExpectedReadBytes();
        var grant = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits)).CreateReadGrant(exactBytes, 3);
        var resolution = fixture.Query(grant);

        await Assert.That(resolution.IsFallback).IsTrue();
        await Assert.That(resolution.DirectoryRevision).IsEqualTo(0L);
        await Assert.That(resolution.Revision).IsEqualTo(0L);
        await Assert.That(resolution.PhysicalShardId).IsEqualTo(AtomicPartitionPlacementQueryViewFixture.ShardId);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(3);
        await Assert.That(grant.ReadBytes).IsEqualTo(exactBytes);
    }

    [Test]
    public async Task AcPquery003OneByteShortGrantFailsWithoutChangingMetadata()
    {
        using var fixture = new AtomicPartitionPlacementQueryViewFixture();
        var before = fixture.CaptureMetadata();
        var exactBytes = fixture.ExpectedReadBytes();
        var grant = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits)).CreateReadGrant(exactBytes - 1, 3);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Query(grant));
        var after = fixture.CaptureMetadata();

        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(grant.ExaminedRecords).IsLessThan(3);
        await Assert.That(AtomicPartitionPlacementQueryViewFixture.SameBytes(before, after)).IsTrue();
    }

    [Test]
    public async Task AcPquery003CancellationStopsBeforeMetadataReadAndUnauthorizedPrincipalNeverEntersCallback()
    {
        using var fixture = new AtomicPartitionPlacementQueryViewFixture();
        using var cancellation = new CancellationTokenSource();
        var before = fixture.CaptureMetadata();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits), cancellationToken: cancellation.Token);
        var grant = budget.CreateReadGrant(fixture.ExpectedReadBytes(), 3);
        await cancellation.CancelAsync();
        var canceled = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Query(grant));
        await Assert.That(canceled).IsNotNull();
        await Assert.That(grant.ExaminedRecords).IsEqualTo(0);

        var denied = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits)).CreateReadGrant(fixture.ExpectedReadBytes(), 3);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Engine.WithQueryView(
            AtomicPartitionPlacementQueryViewFixture.DeniedId,
            AtomicPartitionPlacementQueryViewFixture.Partition, AtomicPartitionPlacementQueryViewFixture.Collection,
            (view, _, _) => DatabaseEngine.ReadAtomicPartitionPlacementForAuthorizedQuery(view,
                AtomicPartitionPlacementQueryViewFixture.Partition, denied)));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.ExaminedRecords).IsEqualTo(0);
        await Assert.That(AtomicPartitionPlacementQueryViewFixture.SameBytes(before,
            fixture.CaptureMetadata())).IsTrue();
    }

    [Test]
    public async Task AcPquery003OrphanRowWithoutHeaderIsCorruptionAndNeverFallsBack()
    {
        using var fixture = new AtomicPartitionPlacementQueryViewFixture();
        fixture.DeleteDirectory();
        var before = fixture.CaptureMetadata();
        var grant = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits)).CreateReadGrant(24_576, 3);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Query(grant));

        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(3);
        await Assert.That(AtomicPartitionPlacementQueryViewFixture.SameBytes(before,
            fixture.CaptureMetadata())).IsTrue();
    }

    [Test]
    public async Task AcPquery003MalformedCatalogDirectoryAndRowFailClosedWithoutMutation()
    {
        await AssertCorruptionAsync(PhysicalShardCatalogRecordSerialization.CatalogKey());
        await AssertCorruptionAsync(AtomicPartitionPlacementSerialization.DirectoryKey());
        await AssertCorruptionAsync(AtomicPartitionPlacementSerialization.RowKey(
            AtomicPartitionPlacementQueryViewFixture.Partition));
    }

    private static async Task AssertCorruptionAsync(byte[] key)
    {
        using var fixture = new AtomicPartitionPlacementQueryViewFixture();
        if (key.SequenceEqual(PhysicalShardCatalogRecordSerialization.CatalogKey()))
        {
            var shard = new PhysicalShardRecord(AtomicPartitionPlacementQueryViewFixture.ShardId,
                AtomicPartitionPlacementQueryViewFixture.Incarnation,
                AtomicPartitionPlacementQueryViewFixture.Voters, 1);
            fixture.ReplaceBytes(key, NativeSerialization.Serialize(new PhysicalShardCatalog(2, 1, shard)));
        }
        else if (key.SequenceEqual(AtomicPartitionPlacementSerialization.DirectoryKey()))
        {
            fixture.ReplaceBytes(key, NativeSerialization.Serialize(new AtomicPartitionPlacementDirectoryV1(2, 0, 0)));
        }
        else
        {
            fixture.ReplaceBytes(key, NativeSerialization.Serialize(new AtomicPartitionPlacementV1(1,
                AtomicPartitionPlacementQueryViewFixture.Partition, Guid.Empty, 0,
                AtomicPartitionPlacementQueryViewFixture.Incarnation,
                AtomicPartitionPlacementQueryViewFixture.Voters, 1)));
        }

        var before = fixture.CaptureMetadata();
        var grant = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(fixture.Engine.Limits)).CreateReadGrant(24_576, 3);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Query(grant));
        var after = fixture.CaptureMetadata();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(AtomicPartitionPlacementQueryViewFixture.SameBytes(before, after)).IsTrue();
    }
}
