using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentMutationImageTests
{
    private const string Collection = "orders";
    private const string DocumentId = "image";
    private const string FirstJson = "{\"value\":\"first\"}";
    private const string SecondJson = "{\"value\":\"second\"}";
    private const string EmptyJson = "{}";
    private const string OutboxSpace = "outbox";
    private const int FirstSequence = 1;
    private const int MutationCount = 3;

    [Test]
    public async Task AcDstore005SequentialSameIdMutationsPersistExactOutboxImagesAndBytes()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var now = TimeProvider.System.GetUtcNow().AddMinutes(1);
        Mutation[] mutations =
        [
            new PutDocument(Collection, DocumentId, FirstJson),
            new PatchDocument(Collection, DocumentId, [new("/value", PatchKind.Set, "\"second\"")], 1),
            new DeleteDocument(Collection, DocumentId, 2)
        ];
        var id = Guid.NewGuid();
        var receipt = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [.. mutations]), id: id, time: now)
            .Get<CommitReceipt>();
        var reference = new EntityRef(db.Partition, Collection, DocumentId);
        DocumentRecord[] images =
        [
            new(reference, 1, FirstJson, new RowAccess(), now),
            new(reference, 2, SecondJson, new RowAccess(), now),
            new(reference, 3, EmptyJson, new RowAccess(), now, Deleted: true)
        ];

        for (var ordinal = 0; ordinal < MutationCount; ordinal++)
        {
            var expected = new OutboxEntry(ordinal + FirstSequence, ordinal, receipt.Token, now,
                mutations[ordinal], receipt.Mutations[ordinal], ordinal == 0 ? null : images[ordinal - 1], images[ordinal]);
            var bytes = db.Store.Read(view => view.ReadOwnedValue(
                KeySpace.Partition(OutboxSpace, db.Partition, ordinal + FirstSequence))!);
            await Assert.That(bytes.SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            await Assert.That(JsonDefaults.Deserialize<OutboxEntry>(bytes).After).IsEqualTo(images[ordinal]);
        }

        await Assert.That(db.Database.GetDocument("root", reference)).IsNull();
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(MutationCount);
    }

    [Test]
    public async Task AcDstore005FailedMissingAndStaleCasKeepImagesAndAllowHealthyNextCommand()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var missing = Submit(db, new PatchDocument(Collection, DocumentId, [new("/value", PatchKind.Set, "1")], 1));
        await Assert.That(missing.Error).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(0);

        db.Commit(new PutDocument(Collection, DocumentId, FirstJson));
        var stale = Submit(db, new DeleteDocument(Collection, DocumentId, 0));
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, Collection, DocumentId))!.Revision).IsEqualTo(1);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(1);

        db.Commit(new DeleteDocument(Collection, DocumentId, 1));
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, Collection, DocumentId))).IsNull();
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(2);
    }

    private static OperationResult Submit(TestDatabase db, Mutation mutation)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [mutation]), id: id);
    }
}
