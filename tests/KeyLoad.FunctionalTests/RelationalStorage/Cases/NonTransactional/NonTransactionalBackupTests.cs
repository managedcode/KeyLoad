using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class NonTransactionalBackupTests
{
    [Test]
    public async Task QuiescentBackupAndFileStorageCopyRestoreBothTreesAndAllowFreshPlainWrites()
    {
        using var fixture = new NonTransactionalTableFixture();
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        (await fixture.ConfigureAsync(transactional: true, NonTransactionalTableFixture.AtomicTable)).Get<ResourceDefinition>();
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(NonTransactionalTableFixture.AtomicTable,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
        var backup = fixture.Destination(NonTransactionalTableFixture.BackupDirectory);
        var cut = await fixture.Store.CreateBackupAsync(backup);
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.Second, NonTransactionalTableFixture.Row(RelationalTestData.Second, NonTransactionalTableFixture.Beta))))
            .Get<CommitReceipt>();
        var copy = fixture.Destination(NonTransactionalTableFixture.CopyDirectory);
        await ZoneTreeBackupCopy.CopyToFileStorageAsync(backup, copy);
        var destination = fixture.Destination(NonTransactionalTableFixture.RestoredDirectory);
        var identity = await ZoneTreeStore.RestoreAsync(copy, destination, UnitExecutionOptions.StorageExecution());
        await Assert.That(identity.DispatchPaused).IsTrue();
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.Store.Identity.NodeId);
        using var restored = NonTransactionalTableFixture.Open(destination);
        await Assert.That(restored.Position).IsEqualTo(cut + 1);
        var database = fixture.Owner.CreateConfiguredDatabase(restored);
        await Assert.That(database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.First))!.Revision).IsEqualTo(1);
        await Assert.That(database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, NonTransactionalTableFixture.AtomicTable, RelationalTestData.First))!.Revision).IsEqualTo(1);
        await Assert.That(database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.Second))).IsNull();
        var id = Guid.NewGuid();
        (await new EmbeddedCoordinator(database).RuntimeSubmitAsync<object>(OperationKind.Batch, id, RelationalTestData.Root,
            new CommandRequest(id, fixture.Partition, [new PutDocument(RelationalTestData.Table,
                RelationalTestData.Second, NonTransactionalTableFixture.Row(RelationalTestData.Second, NonTransactionalTableFixture.Beta))])))
            .Get<CommitReceipt>();
    }
}
