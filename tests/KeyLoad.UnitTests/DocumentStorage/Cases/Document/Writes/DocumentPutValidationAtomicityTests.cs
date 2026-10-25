namespace KeyLoad.UnitTests.DocumentStorage;

internal sealed class DocumentPutValidationAtomicityTests
{
    private const int SystemTextJsonDefaultDepth = 64;
    private const string Collection = DocumentCrudFixture.Collection;
    private const string IndexName = DocumentCrudFixture.IndexName;
    private const string IndexPath = "/value";
    private const string StagedId = "staged-before-validation";
    private const string InvalidId = "invalid-put";
    private const string StagedValue = "staged-value";

    [Test]
    public async Task AcDstore001PutRejectsDuplicateAndOverDepthJsonAfterStagedIndexMutation()
    {
        await AssertRejectedPutBatch(new(), "{not-json", ErrorCode.Validation);
        await AssertRejectedPutBatch(new(), "{\"value\":\"a\",\"value\":\"b\"}", ErrorCode.Validation);
        await AssertRejectedPutBatch(new(),
            DocumentCrudFixture.NestedObjectJson(SystemTextJsonDefaultDepth + 1), ErrorCode.Validation);
    }

    private static async Task AssertRejectedPutBatch(DatabaseLimits limits, string rejectedJson, ErrorCode expected)
    {
        using var database = new TestDatabase(limits);
        database.Configure(Collection, ResourceKind.Collection,
            indexes: [new(IndexName, [IndexPath], Unique: true)]);
        var result = DocumentCrudFixture.Submit(database,
            new PutDocument(Collection, StagedId, "{\"value\":\"staged-value\"}", 0),
            new PutDocument(Collection, InvalidId, rejectedJson, 0));

        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(expected);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, StagedId))).IsNull();
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, InvalidId))).IsNull();
        await DocumentCrudFixture.AssertIndexedIds(database, "value", IndexName, StagedValue, []);
    }
}
