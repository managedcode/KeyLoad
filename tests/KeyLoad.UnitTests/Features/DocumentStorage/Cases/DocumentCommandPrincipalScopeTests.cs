namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentCommandPrincipalScopeTests
{
    private const string Collection = "orders";
    private const string FirstPrincipal = "scope-writer-one";
    private const string SecondPrincipal = "scope-writer-two";
    private const string FirstDocument = "scope-one";
    private const string SecondDocument = "scope-two";
    private const string FirstJson = "{\"owner\":\"one\"}";
    private const string SecondJson = "{\"owner\":\"two\"}";
    private const string ChangedFirstJson = "{\"owner\":\"changed-one\"}";
    private const string ChangedSecondJson = "{\"owner\":\"changed-two\"}";
    private const string PutDocumentKind = "putDocument";

    [Test]
    public async Task AcDstore007PersistedPrincipalsKeepIndependentOutcomesForOneCommandId()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        DocumentCommandOutcomeTestSupport.ConfigureWriter(database, FirstPrincipal);
        DocumentCommandOutcomeTestSupport.ConfigureWriter(database, SecondPrincipal);
        var commandId = Guid.NewGuid();
        var initialOutbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        var firstCommand = new CommandRequest(commandId, database.Partition,
            [new PutDocument(Collection, FirstDocument, FirstJson, 0)]);
        var secondCommand = new CommandRequest(commandId, database.Partition,
            [new PutDocument(Collection, SecondDocument, SecondJson, 0)]);

        var firstReceipt = database.Submit(OperationKind.Batch, firstCommand, FirstPrincipal, commandId).Get<CommitReceipt>();
        var secondReceipt = database.Submit(OperationKind.Batch, secondCommand, SecondPrincipal, commandId).Get<CommitReceipt>();
        await AssertReceipt(firstReceipt, commandId, FirstDocument);
        await AssertReceipt(secondReceipt, commandId, SecondDocument);
        await AssertReceipt(database.Database.Outcome(FirstPrincipal, commandId)!.Get<CommitReceipt>(), commandId, FirstDocument);
        await AssertReceipt(database.Database.Outcome(SecondPrincipal, commandId)!.Get<CommitReceipt>(), commandId, SecondDocument);
        var firstOutcomeBytes = DocumentCommandOutcomeTestSupport.OutcomeBytes(database, FirstPrincipal, commandId);
        var secondOutcomeBytes = DocumentCommandOutcomeTestSupport.OutcomeBytes(database, SecondPrincipal, commandId);
        var committedOutbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxTail(committedOutbox))
            .IsEqualTo(DocumentCommandOutcomeTestSupport.OutboxTail(initialOutbox) + 2);
        await Assert.That(firstOutcomeBytes.AsSpan().SequenceEqual(secondOutcomeBytes)).IsFalse();

        await VerifyChangedContentAndReplayAsync(database, commandId, firstCommand, secondCommand,
            firstOutcomeBytes, secondOutcomeBytes, committedOutbox);
    }

    private static async Task VerifyChangedContentAndReplayAsync(TestDatabase database, Guid commandId,
        CommandRequest firstCommand, CommandRequest secondCommand, byte[] firstOutcomeBytes,
        byte[] secondOutcomeBytes, OutboxSnapshot committedOutbox)
    {
        var firstConflict = database.Submit(OperationKind.Batch,
            firstCommand with { Mutations = [new PutDocument(Collection, FirstDocument, ChangedFirstJson, 1)] },
            FirstPrincipal, commandId);
        var secondConflict = database.Submit(OperationKind.Batch,
            secondCommand with { Mutations = [new PutDocument(Collection, SecondDocument, ChangedSecondJson, 1)] },
            SecondPrincipal, commandId);
        await Assert.That(firstConflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(secondConflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutcomeBytes(database, FirstPrincipal, commandId)
            .AsSpan().SequenceEqual(firstOutcomeBytes)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutcomeBytes(database, SecondPrincipal, commandId)
            .AsSpan().SequenceEqual(secondOutcomeBytes)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(committedOutbox,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
        await AssertDocument(database, FirstDocument, FirstJson);
        await AssertDocument(database, SecondDocument, SecondJson);

        var firstReplay = database.Submit(OperationKind.Batch, firstCommand, FirstPrincipal, commandId).Get<CommitReceipt>();
        var secondReplay = database.Submit(OperationKind.Batch, secondCommand, SecondPrincipal, commandId).Get<CommitReceipt>();
        await AssertReceipt(firstReplay, commandId, FirstDocument);
        await AssertReceipt(secondReplay, commandId, SecondDocument);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutcomeBytes(database, FirstPrincipal, commandId)
            .AsSpan().SequenceEqual(firstOutcomeBytes)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutcomeBytes(database, SecondPrincipal, commandId)
            .AsSpan().SequenceEqual(secondOutcomeBytes)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(committedOutbox,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
    }

    private static async Task AssertReceipt(CommitReceipt receipt, Guid commandId, string documentId)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(PutDocumentKind);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(Collection);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(documentId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1);
    }

    private static async Task AssertDocument(TestDatabase database, string documentId, string json)
    {
        var document = database.Database.GetDocument("root", new(database.Partition, Collection, documentId));
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(1);
        await Assert.That(document.Json).IsEqualTo(json);
    }
}
