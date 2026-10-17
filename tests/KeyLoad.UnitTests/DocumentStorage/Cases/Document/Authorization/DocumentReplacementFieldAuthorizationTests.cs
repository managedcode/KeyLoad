namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentReplacementFieldAuthorizationTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string DocumentId = DocumentCrudFixture.DocumentId;
    private const string SecretPath = DocumentCrudFixture.SecretPath;
    private const string SecretIndexName = DocumentCrudFixture.SecretIndexName;
    private const string RawUseGrant = DocumentCrudFixture.RawUseGrant;
    private const string WriteGrant = DocumentCrudFixture.WriteGrant;
    private const string BeforeJson = "{\"secret\":\"before\"}";
    private const string DeniedJson = "{\"secret\":\"denied\"}";
    private const string ReplacedJson = "{\"secret\":\"replaced\"}";
    private const string MissingWrite = "replacement-without-field-write";
    private const string FullFieldAccess = "replacement-with-field-write";
    private const int FirstRevision = 1;
    private const int ReplacedRevision = 2;

    [Test]
    public async Task AcDstore003ProtectedExplicitReplacementRequiresPersistedFieldWriteGrant()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection,
            indexes: [new(SecretIndexName, [SecretPath])],
            fields: [new(SecretPath, "secret", DocumentCrudFixture.RawReadGrant, RawUseGrant, WriteGrant)]);
        database.Commit(new PutDocument(Collection, DocumentId, BeforeJson, 0));
        DocumentCrudFixture.ConfigurePrincipal(database, MissingWrite, Collection,
            Capability.DocumentsWrite, [DocumentCrudFixture.RawReadGrant, RawUseGrant]);
        DocumentCrudFixture.ConfigurePrincipal(database, FullFieldAccess, Collection,
            Capability.DocumentsWrite, [DocumentCrudFixture.RawReadGrant, RawUseGrant, WriteGrant]);

        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        var before = DocumentCrudFixture.ReadRecord(database, reference)!;
        var denied = DocumentCrudFixture.Submit(database, MissingWrite,
            new PutDocument(Collection, DocumentId, DeniedJson, FirstRevision, ExplicitReplacement: true));
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)).IsEqualTo(before);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "before", [DocumentId]);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "denied", []);

        var authorized = DocumentCrudFixture.Submit(database, FullFieldAccess,
            new PutDocument(Collection, DocumentId, ReplacedJson, FirstRevision, ExplicitReplacement: true));
        await Assert.That(authorized.Error).IsNull();
        var replaced = DocumentCrudFixture.ReadRecord(database, reference)!;
        await Assert.That(replaced.Revision).IsEqualTo(ReplacedRevision);
        await Assert.That(replaced.Json).IsEqualTo(ReplacedJson);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "before", []);
        await DocumentCrudFixture.AssertIndexedIds(database, "secret", SecretIndexName, "replaced", [DocumentId]);
    }
}
