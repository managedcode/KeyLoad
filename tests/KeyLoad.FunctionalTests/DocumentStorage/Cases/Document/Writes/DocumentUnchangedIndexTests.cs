using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentUnchangedIndexTests
{
    private const string Collection = "unchanged-index";
    private const string Index = "by-number";
    private const string Entity = "one";
    private const string Original = "{\"number\":1,\"body\":\"before\"}";
    private const string Updated = "{\"number\":1,\"body\":\"after\"}";
    private const string Changed = "{\"number\":2,\"body\":\"after\"}";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UpdatingOnlyTheBodyAddsNoIndexMutations(bool unique)
    {
        var plainMutations = 0;
        var indexedMutations = 0;
        using var plain = new TestDatabase(storeFaultObserver: (stage, _, count) =>
        {
            if (stage == CommitStage.ApplyCompleted)
            {
                plainMutations = count;
            }
        });
        using var indexed = new TestDatabase(storeFaultObserver: (stage, _, count) =>
        {
            if (stage == CommitStage.ApplyCompleted)
            {
                indexedMutations = count;
            }
        });
        plain.Configure(Collection, ResourceKind.Collection);
        indexed.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, ["/number"], Unique: unique)]);
        plain.Commit(new PutDocument(Collection, Entity, Original, 0));
        indexed.Commit(new PutDocument(Collection, Entity, Original, 0));
        plain.Commit(new PutDocument(Collection, Entity, Updated, 1, ExplicitReplacement: true));
        indexed.Commit(new PutDocument(Collection, Entity, Updated, 1, ExplicitReplacement: true));
        await Assert.That(plainMutations).IsGreaterThan(0);
        await Assert.That(indexedMutations).IsEqualTo(plainMutations);
        await Assert.That(indexed.Database.GetDocument("root", new(indexed.Partition, Collection, Entity))!.Json)
            .IsEqualTo(Updated);
        await Assert.That(indexed.Database.GetDocument("root", new(indexed.Partition, Collection, Entity))!.Revision)
            .IsEqualTo(2L);
        await IndexOwnerAsync(indexed.Store, indexed.Partition, "index", 1m, Entity);
        if (unique)
        {
            await IndexOwnerAsync(indexed.Store, indexed.Partition, "unique", 1m, Entity);
        }
    }

    [Test]
    public async Task UnchangedUniqueOwnershipSurvivesConflictsChangesDeletionAndColdOpen()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection, indexes: [new(Index, ["/number"], Unique: true)]);
        database.Commit(new PutDocument(Collection, Entity, Original, 0));
        database.Commit(new PutDocument(Collection, Entity, Updated, 1, ExplicitReplacement: true));
        var duplicate = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(Collection, "two", Original, 0)]);
        var denied = database.Submit(OperationKind.Batch, duplicate, id: duplicate.CommandId);
        await Assert.That(Errors.Code(denied.Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Database.GetDocument("root", new(database.Partition, Collection, "two"))).IsNull();
        database.Commit(new PutDocument(Collection, Entity, Changed, 2, ExplicitReplacement: true),
            new PutDocument(Collection, "two", Original, 0));
        await IndexOwnerAsync(database.Store, database.Partition, "unique", 1m, "two");
        await IndexOwnerAsync(database.Store, database.Partition, "unique", 2m, Entity);
        database.Commit(new DeleteDocument(Collection, Entity, 3), new PutDocument(Collection, "three", Changed, 0));
        database.JoinOwnedResources();
        using var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution);
        var owner = database.CreateConfiguredDatabase(cold);
        await Assert.That(owner.GetDocument("root", new(database.Partition, Collection, Entity))).IsNull();
        await Assert.That(owner.GetDocument("root", new(database.Partition, Collection, "two"))!.Json).IsEqualTo(Original);
        await Assert.That(owner.GetDocument("root", new(database.Partition, Collection, "three"))!.Json).IsEqualTo(Changed);
        await IndexOwnerAsync(cold, database.Partition, "index", 1m, "two");
        await IndexOwnerAsync(cold, database.Partition, "index", 2m, "three");
        await IndexOwnerAsync(cold, database.Partition, "unique", 1m, "two");
        await IndexOwnerAsync(cold, database.Partition, "unique", 2m, "three");
    }

    private static async Task IndexOwnerAsync(ZoneTreeStore store, PartitionRef partition, string family, decimal number, string id)
    {
        var key = family == "index" ? KeySpace.Partition(family, partition, Collection, Index, number, id)
            : KeySpace.Partition(family, partition, Collection, Index, number);
        await Assert.That(store.Read(view => view.GetRecord<string>(key))).IsEqualTo(id);
    }
}
