using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class NonTransactionalTableTests
{
    [Test]
    public async Task PlainRowsAndTransactionalOutcomesKeepTheirNativeTreesAndSurviveColdOpen()
    {
        using var fixture = new NonTransactionalTableFixture();
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        var id = Guid.NewGuid();
        var input = new CommandRequest(id, fixture.Partition,
            [new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row())]);
        var first = (await fixture.SubmitAsync(OperationKind.Batch, input, id)).Get<CommitReceipt>();
        var position = fixture.Store.Position;
        var replay = (await fixture.SubmitAsync(OperationKind.Batch, input, id)).Get<CommitReceipt>();
        await Assert.That(replay).IsEquivalentTo(first);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(fixture.RowKey()))!;
        var outcome = fixture.Store.Read(view => view.ReadOwnedValue(fixture.OutcomeKey(id)))!;
        fixture.Store.Dispose();
        using (var plain = ZoneTreeTreeFactory.Plain(fixture.Destination(NonTransactionalTableFixture.PlainDirectory)).Open())
        using (var transactional = ZoneTreeTreeFactory.Open(new(fixture.Owner.Directory), requireExisting: true))
        using (var snapshot = transactional.BeginReadOnlyTransaction())
        {
            await Assert.That(plain.TryGet(fixture.RowKey(), out var stored)).IsTrue();
            await Assert.That(stored.Value.ToArray()).IsEquivalentTo(row, CollectionOrdering.Matching);
            await Assert.That(snapshot.TryGet(fixture.RowKey(), out _)).IsFalse();
            await Assert.That(plain.TryGet(fixture.OutcomeKey(id), out _)).IsFalse();
            await Assert.That(snapshot.TryGet(fixture.OutcomeKey(id), out var retained)).IsTrue();
            await Assert.That(retained.ToArray()).IsEquivalentTo(outcome, CollectionOrdering.Matching);
        }
        using var cold = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        var database = fixture.Owner.CreateConfiguredDatabase(cold);
        await Assert.That(database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.First))!.Json).IsEqualTo(RelationalTestData.Row());
        var third = Guid.NewGuid();
        (await new EmbeddedCoordinator(database).RuntimeSubmitAsync(OperationKind.Batch, third, RelationalTestData.Root,
            new CommandRequest(third, fixture.Partition, [new PutDocument(RelationalTestData.Table,
                RelationalTestData.Second, NonTransactionalTableFixture.Row(RelationalTestData.Second, NonTransactionalTableFixture.Beta))])))
            .Get<CommitReceipt>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MixedTableModesRefuseBothOrdersBeforeEffectsAndHealthyCommandsContinue(bool plainFirst)
    {
        using var fixture = new NonTransactionalTableFixture();
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        (await fixture.ConfigureAsync(transactional: true, NonTransactionalTableFixture.AtomicTable)).Get<ResourceDefinition>();
        Mutation plain = new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row());
        Mutation atomic = new PutDocument(NonTransactionalTableFixture.AtomicTable, RelationalTestData.First, RelationalTestData.Row());
        var failed = await fixture.BatchAsync(Guid.NewGuid(), plainFirst ? [plain, atomic] : [atomic, plain]);
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.First))).IsNull();
        await Assert.That(fixture.Database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, NonTransactionalTableFixture.AtomicTable, RelationalTestData.First))).IsNull();
        (await fixture.BatchAsync(Guid.NewGuid(), plain)).Get<CommitReceipt>();
        (await fixture.BatchAsync(Guid.NewGuid(), atomic)).Get<CommitReceipt>();
    }

    [Test]
    public async Task ConstraintFailureDiscardsPlainStagingAndUniqueIndexContinuesAfterDelete()
    {
        using var fixture = new NonTransactionalTableFixture();
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
        var failed = await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.Second, RelationalTestData.Row(RelationalTestData.Second)));
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.Second))).IsNull();
        (await fixture.BatchAsync(Guid.NewGuid(), new DeleteDocument(RelationalTestData.Table, RelationalTestData.First, 1),
            new PutDocument(RelationalTestData.Table, RelationalTestData.Second, RelationalTestData.Row(RelationalTestData.Second))))
            .Get<CommitReceipt>();
        await Assert.That(fixture.Database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.First))).IsNull();
        await Assert.That(fixture.Database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.Second))!.Revision).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TableModeCannotChangeThroughResourcePolicyUpdates(bool transactional)
    {
        using var fixture = new NonTransactionalTableFixture();
        var original = (await fixture.ConfigureAsync(transactional)).Get<ResourceDefinition>();
        var key = KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, original.Name);
        var bytes = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var request = new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId,
            original with { Transactional = !transactional, SchemaVersion = original.SchemaVersion + 1 })
        {
            ExpectedSchemaVersion = original.SchemaVersion
        };
        var failed = await fixture.SubmitAsync(OperationKind.ConfigureResource, request);
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(key))!)
            .IsEquivalentTo(bytes, CollectionOrdering.Matching);
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
    }

    [Test]
    [Arguments(ResourceKind.Graph)]
    [Arguments(ResourceKind.StreamSet)]
    [Arguments(ResourceKind.WorkQueue)]
    [Arguments(ResourceKind.Topic)]
    [Arguments(ResourceKind.BlobStore)]
    public async Task NonTableResourcesRejectPlainModeBeforeCreationAndHealthyTableCreationFollows(ResourceKind kind)
    {
        using var fixture = new NonTransactionalTableFixture();
        var definition = new ResourceDefinition(RelationalTestData.Table, kind, fixture.Partition.TransactionDomainId)
        {
            Transactional = false
        };
        var result = await fixture.SubmitAsync(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition));
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Resource(
            fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition.Name)))).IsNull();
        await Assert.That((await fixture.ConfigureAsync()).Get<ResourceDefinition>().Transactional).IsFalse();
    }
}
