namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentExactContentTests
{
    private const string Collection = "orders";
    private const string DocumentId = "exact-content";
    private const string Root = "root";
    private const string LiteralJson = "{\"z\":\"Привіт é 😀 \\\"quoted\\\"\", \"a\":1.2300}\n";
    private const string EscapedJson = " {\"z\":\"\\u041F\\uD83D\\uDE00\",\"a\":1e+01} ";

    [Test]
    [Arguments(LiteralJson)]
    [Arguments(EscapedJson)]
    public async Task AcDstore008PutNativeRecordAndSameCommandReplayRetainExactCallerText(string json)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
            [new PutDocument(Collection, DocumentId, json, 0)]);
        var receipt = database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var record = DocumentCrudFixture.ReadRecord(database, reference)!;
        var native = NativeSerialization.Deserialize<DocumentRecord>(NativeSerialization.Serialize(record));
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);

        await Assert.That(database.Database.GetDocument(Root, reference)!.Json).IsEqualTo(json);
        await Assert.That(record.Json).IsEqualTo(json);
        await Assert.That(native.Json).IsEqualTo(json);
        var replay = database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        await Assert.That(replay.CommandId).IsEqualTo(receipt.CommandId);
        await Assert.That(replay.Token).IsEqualTo(receipt.Token);
        await Assert.That(database.Database.GetDocument(Root, reference)!.Json).IsEqualTo(json);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)!.Revision).IsEqualTo(1);
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(outbox,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
    }

    [Test]
    public async Task AcDstore008ReplacementRetainsItsNewLiteralText()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        database.Commit(new PutDocument(Collection, DocumentId, LiteralJson, 0));
        database.Commit(new PutDocument(Collection, DocumentId, EscapedJson, 1, ExplicitReplacement: true));

        var actual = database.Database.GetDocument(Root, reference)!;
        await Assert.That(actual.Json).IsEqualTo(EscapedJson);
        await Assert.That(actual.Revision).IsEqualTo(2);
    }

    [Test]
    [Arguments("{\"a\":1,\"a\":2}")]
    [Arguments("{\"a\":1e1000}")]
    public async Task AcDstore008DuplicateMembersAndDecimalOverflowStillFailWithoutDocumentEffects(string json)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        var outbox = DocumentCommandOutcomeTestSupport.CaptureOutbox(database);
        var result = DocumentCrudFixture.Submit(database, new PutDocument(Collection, DocumentId, json));

        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Database.GetDocument(Root, reference)).IsNull();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(outbox,
            DocumentCommandOutcomeTestSupport.CaptureOutbox(database))).IsTrue();
    }
}
