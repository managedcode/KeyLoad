namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentDeleteIndexAuthorizationTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string DocumentId = DocumentCrudFixture.DocumentId;
    private const string SecretPath = DocumentCrudFixture.SecretPath;
    private const string SecretIndexName = DocumentCrudFixture.SecretIndexName;
    private const string RawUseGrant = DocumentCrudFixture.RawUseGrant;
    private const string WriteGrant = DocumentCrudFixture.WriteGrant;
    private const string BeforeJson = "{\"secret\":\"before\"}";
    private const string MissingIndexUse = "delete-without-index-use";
    private const string IndexUseWithoutFieldWrite = "delete-with-index-use";
    private const int FirstRevision = 1;
    private const int DeletedRevision = 2;
    private const string TombstoneJson = "{}";

    [Test]
    public async Task AcDstore003DeleteRequiresIndexUseButDoesNotRequireFieldWrite()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection,
            indexes: [new(SecretIndexName, [SecretPath])],
            fields: [new(SecretPath, "secret", DocumentCrudFixture.RawReadGrant, RawUseGrant, WriteGrant)]);
        database.Commit(new PutDocument(Collection, DocumentId, BeforeJson, 0));
        DocumentCrudFixture.ConfigurePrincipal(database, MissingIndexUse, Collection,
            Capability.DocumentsWrite, [WriteGrant]);
        DocumentCrudFixture.ConfigurePrincipal(database, IndexUseWithoutFieldWrite, Collection,
            Capability.DocumentsWrite, [RawUseGrant]);

        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        var before = DocumentCrudFixture.ReadRecord(database, reference)!;
        var denied = DocumentCrudFixture.Submit(database, MissingIndexUse,
            new DeleteDocument(Collection, DocumentId, FirstRevision));
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)).IsEqualTo(before);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "before", [DocumentId]);

        var authorized = DocumentCrudFixture.Submit(database, IndexUseWithoutFieldWrite,
            new DeleteDocument(Collection, DocumentId, FirstRevision));
        await Assert.That(authorized.Error).IsNull();
        var tombstone = DocumentCrudFixture.ReadRecord(database, reference)!;
        await Assert.That(tombstone.Revision).IsEqualTo(DeletedRevision);
        await Assert.That(tombstone.Deleted).IsTrue();
        await Assert.That(tombstone.Json).IsEqualTo(TombstoneJson);
        await Assert.That(database.Database.GetDocument(DocumentCrudFixture.Root, reference)).IsNull();
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "before", []);
    }
}
