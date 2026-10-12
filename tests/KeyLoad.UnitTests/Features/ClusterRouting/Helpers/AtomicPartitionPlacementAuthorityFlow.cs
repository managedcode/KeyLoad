using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementAuthorityFlow
{
    internal const string Collection = "placement-authority-documents";
    private const int CurrentVersion = 1;
    private const long UnassignedDirectoryRevision = 0;
    private const int MalformedVersion = 2;
    private const long StalePlacementEpoch = 2;
    private const int MetadataPointReads = 3;
    private const int PublishedLineagePointRead = 1;
    private const long InitialDirectoryRevision = 1;
    private const long HealthyDirectoryRevision = 2;
    private static readonly Guid ForeignOwner = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");
    private static readonly Guid OriginalCommand = Guid.Parse("eeddbbcc-aabb-9988-7766-554433221100");
    private static readonly Guid HealthyCommand = Guid.Parse("eeddbbcc-aabb-9988-7766-554433221101");

    internal static async Task RunAsync(PhysicalShardCatalogFixture fixture)
    {
        AtomicPartitionPlacementTestSupport.Bootstrap(fixture);
        ConfigureCollection(fixture);
        var partition = AtomicPartitionPlacementTestSupport.First;
        var first = new BindAtomicPartitionPlacementRequest(CurrentVersion, UnassignedDirectoryRevision, partition,
            AtomicPartitionPlacementTestSupport.ShardId);
        await Assert.That(fixture.Database.Apply(Operation(fixture, first, Guid.NewGuid())).Get<bool>()).IsTrue();
        var row = fixture.Store.Read(view => view.GetRecord<AtomicPartitionPlacementV1>(
            AtomicPartitionPlacementTestSupport.RowKey(partition)))!;
        var original = Operation(fixture, first with { ExpectedRevision = InitialDirectoryRevision }, OriginalCommand);
        await RefuseAndRepairAsync(fixture, row with { Version = MalformedVersion, PhysicalShardId = ForeignOwner }, row, original);
        await RefuseAndRepairAsync(fixture, row with
        {
            Partition = AtomicPartitionPlacementTestSupport.SameSuffixOtherTenant,
            PhysicalShardId = ForeignOwner
        }, row, original);
        await RefuseAndRepairAsync(fixture, row with { PlacementEpoch = StalePlacementEpoch }, row, original, requiresLineage: true);
        await CompleteAndReplayAsync(fixture, original);
        await ContinueHealthyAsync(fixture);
    }

    private static void ConfigureCollection(PhysicalShardCatalogFixture fixture)
    {
        var partition = AtomicPartitionPlacementTestSupport.First;
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.ConfigureResource,
            PhysicalShardCatalogFixture.RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));
        _ = fixture.Database.Apply(operation).Get<ResourceDefinition>();
    }

    private static async Task WitnessQueryRefusalAsync(PhysicalShardCatalogFixture fixture, bool requiresLineage)
    {
        var grant = AtomicPartitionPlacementAuthorityAssertions.CreateGrant(fixture);
        var query = Assert.ThrowsExactly<KeyLoadException>(() =>
            AtomicPartitionPlacementAuthorityAssertions.Query(fixture, grant));
        var expected = requiresLineage ? ErrorCode.BudgetExceeded : ErrorCode.Corruption;
        await Assert.That(query.Code).IsEqualTo(expected);
        await Assert.That(grant.ExaminedRecords).IsEqualTo(MetadataPointReads);
        await Assert.That(grant.ReadBytes).IsEqualTo(AtomicPartitionPlacementAuthorityAssertions.ExpectedReadBytes(fixture));
        if (!requiresLineage)
        { return; }
        await Assert.That(AtomicPartitionPlacementAuthorityAssertions.LineageBytes(fixture)).IsNull();
        var lineageGrant = AtomicPartitionPlacementAuthorityAssertions.CreateLineageGrant(fixture);
        var lineageRefusal = Assert.ThrowsExactly<KeyLoadException>(() =>
            AtomicPartitionPlacementAuthorityAssertions.Query(fixture, lineageGrant));
        await Assert.That(lineageRefusal.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(lineageGrant.ExaminedRecords).IsEqualTo(MetadataPointReads + PublishedLineagePointRead);
        await Assert.That(lineageGrant.ReadBytes).IsEqualTo(AtomicPartitionPlacementAuthorityAssertions.ExpectedLineageReadBytes(fixture));
    }

    private static async Task RefuseAndRepairAsync(PhysicalShardCatalogFixture fixture,
        AtomicPartitionPlacementV1 corrupt, AtomicPartitionPlacementV1 repaired, ReplicatedOperation original,
        bool requiresLineage = false)
    {
        ReplaceRow(fixture, corrupt);
        var image = AtomicPartitionPlacementAuthorityAssertions.Capture(fixture);
        var cut = fixture.Store.Position;
        await WitnessQueryRefusalAsync(fixture, requiresLineage);
        var apply = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(original));
        await Assert.That(apply.Code).IsEqualTo(ErrorCode.Corruption);
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        fixture.Reopen();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        var cold = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(original));
        await Assert.That(cold.Code).IsEqualTo(ErrorCode.Corruption);
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        ReplaceRow(fixture, repaired);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture,
            AtomicPartitionPlacementTestSupport.First, directoryRevision: InitialDirectoryRevision);
        var healthyGrant = AtomicPartitionPlacementAuthorityAssertions.CreateGrant(fixture);
        var healthyQuery = AtomicPartitionPlacementAuthorityAssertions.Query(fixture, healthyGrant);
        var expected = new AtomicPartitionPlacementResolution(CurrentVersion, repaired.Partition,
            repaired.PhysicalShardId, repaired.Incarnation, repaired.VoterIds, repaired.PlacementEpoch,
            InitialDirectoryRevision, repaired.Revision, false);
        await Assert.That(NativeSerialization.Serialize(healthyQuery)
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(healthyGrant.ExaminedRecords).IsEqualTo(MetadataPointReads);
        await Assert.That(healthyGrant.ReadBytes).IsEqualTo(
            AtomicPartitionPlacementAuthorityAssertions.ExpectedReadBytes(fixture));
    }

    private static async Task CompleteAndReplayAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original)
    {
        var result = fixture.Database.Apply(original);
        await Assert.That(result.Get<bool>()).IsTrue();
        var image = AtomicPartitionPlacementAuthorityAssertions.Capture(fixture);
        var cut = fixture.Store.Position;
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(original))
            .SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        fixture.Reopen();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(original))
            .SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture,
            AtomicPartitionPlacementTestSupport.First, directoryRevision: InitialDirectoryRevision);
    }

    private static async Task ContinueHealthyAsync(PhysicalShardCatalogFixture fixture)
    {
        var partition = AtomicPartitionPlacementTestSupport.SameSuffixOtherTenant;
        var request = new BindAtomicPartitionPlacementRequest(CurrentVersion, InitialDirectoryRevision, partition,
            AtomicPartitionPlacementTestSupport.ShardId);
        var operation = Operation(fixture, request, HealthyCommand);
        var result = fixture.Database.Apply(operation);
        await Assert.That(result.Get<bool>()).IsTrue();
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture, partition, directoryRevision: HealthyDirectoryRevision);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture,
            AtomicPartitionPlacementTestSupport.First, directoryRevision: HealthyDirectoryRevision);
        var image = AtomicPartitionPlacementAuthorityAssertions.Capture(fixture);
        var cut = fixture.Store.Position;
        fixture.Reopen();
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(operation))
            .SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await AtomicPartitionPlacementAuthorityAssertions.UnchangedAsync(fixture, image, cut);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture, partition, directoryRevision: HealthyDirectoryRevision);
        await AtomicPartitionPlacementAuthorityAssertions.WitnessAsync(fixture,
            AtomicPartitionPlacementTestSupport.First, directoryRevision: HealthyDirectoryRevision);
    }

    private static void ReplaceRow(PhysicalShardCatalogFixture fixture, AtomicPartitionPlacementV1 row)
        => fixture.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementTestSupport.RowKey(
                AtomicPartitionPlacementTestSupport.First), row);
            return true;
        });

    private static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture,
        BindAtomicPartitionPlacementRequest request, Guid commandId)
        => fixture.Database.CreateNativeOperation(OperationKind.BindAtomicPartitionPlacement,
            commandId, PhysicalShardCatalogFixture.RootPrincipalId,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
}
