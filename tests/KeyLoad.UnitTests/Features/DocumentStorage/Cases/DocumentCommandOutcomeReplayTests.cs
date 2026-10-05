namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentCommandOutcomeReplayTests
{
    private const string Writer = "outcome-writer";
    private const string Collection = "orders";
    private const string DocumentId = "precondition-replay";
    private const string OriginalJson = "{\"value\":1}";
    private const string RevisionConflictDetail = "The expected revision does not match.";

    [Test]
    public async Task AcDstore007FailedRevisionOutcomeReplaysAfterPreconditionBecomesSatisfiable()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        DocumentCommandOutcomeTestSupport.ConfigureWriter(database, Writer);
        var initialOutbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        var failedId = Guid.NewGuid();
        var failedCommand = new CommandRequest(failedId, database.Partition,
            [new PutDocument(Collection, DocumentId, OriginalJson, 1)]);
        var failed = database.Submit(OperationKind.Batch, failedCommand, Writer, failedId);

        await Assert.That(failed.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(failed.SafeDetail).IsEqualTo(RevisionConflictDetail);
        var failedOutcomeBytes = DocumentCommandOutcomeTestSupport.OutcomeBytes(database, Writer, failedId);
        await Assert.That(OutcomeStoreOracle.ReadPartition(database.Store, database.Partition, Writer, failedId)!.SafeDetail).IsEqualTo(RevisionConflictDetail);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(initialOutbox,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
        await Assert.That(database.Database.GetDocument("root", new(database.Partition, Collection, DocumentId))).IsNull();
        var beforeCreate = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(initialOutbox, beforeCreate)).IsTrue();

        var createId = Guid.NewGuid();
        var created = database.Submit(OperationKind.Batch,
            new CommandRequest(createId, database.Partition,
                [new PutDocument(Collection, DocumentId, OriginalJson, 0)]), Writer, createId).Get<CommitReceipt>();
        await Assert.That(created.Mutations).HasSingleItem();
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1);

        var afterCreate = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        var replay = database.Submit(OperationKind.Batch, failedCommand, Writer, failedId);
        var documentAfterReplay = database.Database.GetDocument("root", new(database.Partition, Collection, DocumentId));

        await Assert.That(replay.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(replay.SafeDetail).IsEqualTo(RevisionConflictDetail);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutcomeBytes(database, Writer, failedId)
            .AsSpan().SequenceEqual(failedOutcomeBytes)).IsTrue();
        await Assert.That(documentAfterReplay!.Revision).IsEqualTo(1);
        await Assert.That(documentAfterReplay.Json).IsEqualTo(OriginalJson);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(afterCreate,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxTail(afterCreate))
            .IsEqualTo(DocumentCommandOutcomeTestSupport.OutboxTail(beforeCreate) + 1);

        var retryId = Guid.NewGuid();
        var retry = database.Submit(OperationKind.Batch,
            new CommandRequest(retryId, database.Partition,
                [new PutDocument(Collection, DocumentId, OriginalJson, 1)]), Writer, retryId).Get<CommitReceipt>();
        await Assert.That(retry.Mutations).HasSingleItem();
        await Assert.That(retry.Mutations[0].Revision).IsEqualTo(2);
        await Assert.That(database.Database.GetDocument("root", new(database.Partition, Collection, DocumentId))!.Revision)
            .IsEqualTo(2);
    }
}
