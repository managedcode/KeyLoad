namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentFieldMutationAuthorizationTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string DocumentId = DocumentCrudFixture.DocumentId;
    private const string OtherDocumentId = DocumentCrudFixture.OtherDocumentId;
    private const string SecretPath = DocumentCrudFixture.SecretPath;
    private const string SecretIndexName = DocumentCrudFixture.SecretIndexName;
    private const string RawReadGrant = DocumentCrudFixture.RawReadGrant;
    private const string RawUseGrant = DocumentCrudFixture.RawUseGrant;
    private const string WriteGrant = DocumentCrudFixture.WriteGrant;
    private const string NoFieldWrite = "field-no-write";
    private const string WriteOnly = "field-write-only";
    private const string FullFieldAccess = "field-full-access";
    private const int FirstRevision = 1;
    private const int SecondRevision = 2;

    [Test]
    public async Task AcDstore003PutAndPatchRequireWriteGrantAndIndexedMutationRequiresUseGrant()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection,
            indexes: [new(SecretIndexName, [SecretPath])],
            fields: [new(SecretPath, "secret", RawReadGrant, RawUseGrant, WriteGrant)]);
        database.Commit(new PutDocument(Collection, DocumentId, "{\"secret\":\"before\"}", 0));
        DocumentCrudFixture.ConfigurePrincipal(database, NoFieldWrite, Collection,
            Capability.DocumentsWrite, []);
        DocumentCrudFixture.ConfigurePrincipal(database, WriteOnly, Collection,
            Capability.DocumentsWrite, [WriteGrant]);
        DocumentCrudFixture.ConfigurePrincipal(database, FullFieldAccess, Collection,
            Capability.DocumentsWrite, [WriteGrant, RawUseGrant]);

        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        var before = DocumentCrudFixture.ReadRecord(database, reference)!;
        var deniedPut = DocumentCrudFixture.Submit(database, NoFieldWrite,
            new PutDocument(Collection, OtherDocumentId, "{\"secret\":\"denied\"}", 0));
        var deniedPatch = DocumentCrudFixture.Submit(database, NoFieldWrite,
            new PatchDocument(Collection, DocumentId, [new(SecretPath, PatchKind.Set, "\"denied\"")], FirstRevision));
        var missingUse = DocumentCrudFixture.Submit(database, WriteOnly,
            new PatchDocument(Collection, DocumentId, [new(SecretPath, PatchKind.Set, "\"denied\"")], FirstRevision));
        await Assert.That(deniedPut.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(deniedPatch.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(missingUse.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)).IsEqualTo(before);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, OtherDocumentId))).IsNull();
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "before", [DocumentId]);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "denied", []);

        var authorizedCreate = DocumentCrudFixture.Submit(database, FullFieldAccess,
            new PutDocument(Collection, OtherDocumentId, "{\"secret\":\"created\"}", 0));
        await Assert.That(authorizedCreate.Error).IsNull();
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, OtherDocumentId))!.Revision).IsEqualTo(FirstRevision);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "created", [OtherDocumentId]);

        var authorizedPatch = DocumentCrudFixture.Submit(database, FullFieldAccess,
            new PatchDocument(Collection, DocumentId, [new(SecretPath, PatchKind.Set, "\"after\"")], FirstRevision));
        await Assert.That(authorizedPatch.Error).IsNull();
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)!.Revision).IsEqualTo(SecondRevision);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "before", []);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "after", [DocumentId]);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)!.Json)
            .IsEqualTo("{\"secret\":\"after\"}");
    }
}
