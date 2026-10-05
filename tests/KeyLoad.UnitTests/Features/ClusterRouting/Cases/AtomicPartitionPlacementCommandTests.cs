using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class AtomicPartitionPlacementCommandTests
{
    private const string FinalPartitionKey = "partition-final";
    private const string OverLimitPartitionKey = "partition-over";
    private static readonly PartitionRef Partition = AtomicPartitionPlacementTestSupport.First;

    [Test]
    public async Task AcPmap002CasRetryAndCommandIdentityPreserveOneAssignment()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var request = Request(0, AtomicPartitionPlacementTestSupport.ShardId);
        var commandId = Guid.Parse("f0e1d2c3-b4a5-9687-8899-aabbccddeeff");
        var original = Operation(fixture, request, commandId);
        var first = fixture.Database.Apply(original);
        var before = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());
        var rowBefore = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.RowKey(Partition));
        var retry = fixture.Database.Apply(original);
        var changed = fixture.Database.Apply(Operation(fixture,
            request with { PhysicalShardId = Guid.NewGuid() }, commandId));
        var stale = Apply(fixture, request with { ExpectedRevision = 0 }, Guid.NewGuid());
        var exactNoop = Apply(fixture, request with { ExpectedRevision = 1 }, Guid.NewGuid());
        var after = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());
        var rowAfter = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.RowKey(Partition));

        await Assert.That(first.Error).IsNull();
        await Assert.That(retry.Error).IsNull();
        await Assert.That(changed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(exactNoop.Error).IsNull();
        await Assert.That(after.SequenceEqual(before)).IsTrue();
        await Assert.That(rowAfter.SequenceEqual(rowBefore)).IsTrue();
    }

    [Test]
    public async Task AcPmap002RejectsNonAdminAndNonDefaultOwnerWithoutMutation()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var reader = new PrincipalRecord("reader", "tenant-a", [], []);
        fixture.AddPrincipal(reader);
        var before = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());

        var denied = fixture.Database.CreateNativeOperation(OperationKind.BindAtomicPartitionPlacement,
            Guid.NewGuid(), reader.Id, fixture.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(Request(0, AtomicPartitionPlacementTestSupport.ShardId)));
        var unsupported = Apply(fixture, Request(0, Guid.NewGuid()), Guid.NewGuid());
        var deniedResult = fixture.Database.Apply(denied);
        var readDenied = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadAtomicPartitionPlacement(reader.Id, AtomicPartitionPlacementTestSupport.Read(Partition)));
        var after = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());

        await Assert.That(deniedResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(readDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(unsupported.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(after.SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task AcPmap002InvalidRequestVersionIsValidationWithoutPlacementWrites()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        var before = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());

        var result = Apply(fixture, Request(0, AtomicPartitionPlacementTestSupport.ShardId) with { Version = 2 },
            Guid.NewGuid());
        var after = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());

        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(after.SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task AcPmap001AcceptsExactCapacityAndRejectsTheNextAssignment()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        AtomicPartitionPlacementTestSupport.SeedAssignments(fixture,
            AtomicPartitionPlacementTestSupport.MaximumAssignments - 1);
        var finalPartition = new PartitionRef("tenant-final", "db", "domain", FinalPartitionKey);
        var final = Apply(fixture, Request(AtomicPartitionPlacementTestSupport.MaximumAssignments - 1,
            AtomicPartitionPlacementTestSupport.ShardId) with { Partition = finalPartition }, Guid.NewGuid());
        var directoryBytes = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());
        var directory = fixture.Store.Read(view => view.GetRecord<AtomicPartitionPlacementDirectoryV1>(
            AtomicPartitionPlacementTestSupport.DirectoryKey()));
        var overLimitPartition = new PartitionRef("tenant-over", "db", "domain", OverLimitPartitionKey);
        var overLimit = Apply(fixture, Request(AtomicPartitionPlacementTestSupport.MaximumAssignments,
            AtomicPartitionPlacementTestSupport.ShardId) with { Partition = overLimitPartition }, Guid.NewGuid());
        var after = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.DirectoryKey());
        var absentRow = AtomicPartitionPlacementTestSupport.ReadValue(fixture,
            AtomicPartitionPlacementTestSupport.RowKey(overLimitPartition));

        await Assert.That(final.Error).IsNull();
        await Assert.That(directory!.Revision).IsEqualTo(AtomicPartitionPlacementTestSupport.MaximumAssignments);
        await Assert.That(directory.ExplicitAssignmentCount).IsEqualTo(AtomicPartitionPlacementTestSupport.MaximumAssignments);
        await Assert.That(overLimit.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(after.SequenceEqual(directoryBytes)).IsTrue();
        await Assert.That(absentRow.Length).IsEqualTo(0);
    }

    private static OperationResult Apply(PhysicalShardCatalogFixture fixture,
        BindAtomicPartitionPlacementRequest request, Guid commandId)
    {
        return fixture.Database.Apply(Operation(fixture, request, commandId));
    }

    private static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture,
        BindAtomicPartitionPlacementRequest request, Guid commandId)
        => fixture.Database.CreateNativeOperation(OperationKind.BindAtomicPartitionPlacement, commandId,
            PhysicalShardCatalogFixture.RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));

    private static BindAtomicPartitionPlacementRequest Request(long revision, Guid shardId)
        => new(1, revision, Partition, shardId);
}
