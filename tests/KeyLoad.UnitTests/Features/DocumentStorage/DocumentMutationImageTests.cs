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
            var actual = NativeSerialization.Deserialize<OutboxEntry>(bytes);

            await Assert.That(actual.Sequence).IsEqualTo(expected.Sequence);
            await Assert.That(actual.Ordinal).IsEqualTo(expected.Ordinal);
            await Assert.That(actual.CommittedAt).IsEqualTo(expected.CommittedAt);
            await AssertCommitTokenEqual(actual.Commit, expected.Commit);
            await AssertMutationEqual(actual.Mutation, expected.Mutation);
            await AssertMutationReceiptEqual(actual.Receipt, expected.Receipt);
            await Assert.That(actual.Before).IsEqualTo(expected.Before);
            await Assert.That(actual.After).IsEqualTo(expected.After);
            await Assert.That(bytes.SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();
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

    private static async Task AssertCommitTokenEqual(CommitToken actual, CommitToken expected)
    {
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.AtomicPartitionId).IsEqualTo(expected.AtomicPartitionId);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.OwnershipEpoch).IsEqualTo(expected.OwnershipEpoch);
    }

    private static async Task AssertMutationReceiptEqual(MutationReceipt actual, MutationReceipt expected)
    {
        await Assert.That(actual.Kind).IsEqualTo(expected.Kind);
        await Assert.That(actual.Resource).IsEqualTo(expected.Resource);
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.CompositionReferences.IsEmpty).IsTrue();
    }

    private static async Task AssertMutationEqual(Mutation actual, Mutation expected)
    {
        await Assert.That(actual.GetType()).IsEqualTo(expected.GetType());
        await Assert.That(actual.Resource).IsEqualTo(expected.Resource);
        switch (actual)
        {
            case PutDocument put:
                await Assert.That(put).IsEqualTo((PutDocument)expected);
                break;
            case PatchDocument patch:
                var expectedPatch = (PatchDocument)expected;
                await Assert.That(patch.Collection).IsEqualTo(expectedPatch.Collection);
                await Assert.That(patch.Id).IsEqualTo(expectedPatch.Id);
                await Assert.That(patch.ExpectedRevision).IsEqualTo(expectedPatch.ExpectedRevision);
                await Assert.That(patch.Patches.SequenceEqual(expectedPatch.Patches)).IsTrue();
                break;
            case DeleteDocument delete:
                await Assert.That(delete).IsEqualTo((DeleteDocument)expected);
                break;
            default:
                throw new InvalidOperationException("The expected document mutation is unsupported.");
        }
    }

}
