namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentScalarIndexMutationTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string DocumentId = DocumentCrudFixture.DocumentId;
    private const string OtherDocumentId = DocumentCrudFixture.OtherDocumentId;
    private const string IndexName = DocumentCrudFixture.IndexName;
    private const string LabelPath = DocumentCrudFixture.LabelPath;
    private const string Alpha = "alpha";
    private const string Beta = "beta";
    private const string Gamma = "gamma";
    private const string Delta = "delta";
    private const string Epsilon = "epsilon";
    private const string Safe = "safe";
    private const string ThirdDocumentId = "order-3";
    private const string OtherPartitionKey = "customer-2";
    private const int InitialRevision = 1;
    private const int ReplacedRevision = 2;
    private const int PatchedRevision = 3;

    [Test]
    public async Task AcDstore002ReplacePatchDeleteAndConflictPreserveScalarIndexImages()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection,
            indexes: [new(IndexName, [LabelPath], Unique: true)]);
        database.Commit(new PutDocument(Collection, DocumentId, "{\"label\":\"alpha\"}", 0),
            new PutDocument(Collection, OtherDocumentId, "{\"label\":\"beta\"}", 0),
            new PutDocument(Collection, ThirdDocumentId, "{\"label\":\"gamma\"}", 0));

        database.Commit(new PutDocument(Collection, DocumentId, "{\"label\":\"delta\"}",
            InitialRevision, ExplicitReplacement: true));
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Alpha, []);
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Delta, [DocumentId]);
        database.Commit(new PatchDocument(Collection, DocumentId,
            [new(LabelPath, PatchKind.Set, "\"epsilon\"")], ReplacedRevision));
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Delta, []);
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Epsilon, [DocumentId]);
        database.Commit(new DeleteDocument(Collection, DocumentId, PatchedRevision));
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Epsilon, []);

        var beforeSecond = DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, OtherDocumentId));
        var beforeThird = DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, ThirdDocumentId));
        var conflict = DocumentCrudFixture.Submit(database,
            new PatchDocument(Collection, OtherDocumentId, [new(LabelPath, PatchKind.Set, "\"safe\"")], 1),
            new PatchDocument(Collection, ThirdDocumentId, [new(LabelPath, PatchKind.Set, "\"safe\"")], 1));
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, OtherDocumentId))).IsEqualTo(beforeSecond);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, ThirdDocumentId))).IsEqualTo(beforeThird);
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Beta, [OtherDocumentId]);
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Gamma, [ThirdDocumentId]);
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Safe, []);

        var otherPartition = database.Partition with { PartitionKey = OtherPartitionKey };
        var sameValueElsewhere = DocumentCrudFixture.SubmitAt(database,
            DocumentCrudFixture.Root, otherPartition,
            new PutDocument(Collection, DocumentId, "{\"label\":\"beta\"}", 0));
        await Assert.That(sameValueElsewhere.Error).IsNull();
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Beta,
            [DocumentId], otherPartition);
        await DocumentCrudFixture.AssertIndexedIds(database, "label", IndexName, Beta,
            [OtherDocumentId]);
    }
}
