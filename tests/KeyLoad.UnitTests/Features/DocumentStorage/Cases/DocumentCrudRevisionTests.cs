namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentCrudRevisionTests
{
    private const int FirstRevision = 1;
    private const int ReplacedRevision = 2;
    private const int PatchedRevision = 3;
    private const int TombstoneRevision = 4;
    private const int RecreatedRevision = 5;
    private const int SingleDeleteRevision = 2;
    private const string Collection = DocumentCrudFixture.Collection;
    private const string DocumentId = DocumentCrudFixture.DocumentId;
    private const string Root = DocumentCrudFixture.Root;

    [Test]
    public async Task AcDstore001PutPatchDeleteAndRecreateAdvanceExactRevisions()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var reference = new EntityRef(database.Partition, Collection, DocumentId);

        database.Commit(new PutDocument(Collection, DocumentId, "{\"value\":\"created\"}", 0));
        var created = database.Database.GetDocument(Root, reference)!;
        await Assert.That(created.Revision).IsEqualTo(FirstRevision);
        await Assert.That(created.Json).IsEqualTo("{\"value\":\"created\"}");
        database.Commit(new PutDocument(Collection, DocumentId, "{\"value\":\"replaced\"}",
            FirstRevision, ExplicitReplacement: true));
        var replaced = database.Database.GetDocument(Root, reference)!;
        await Assert.That(replaced.Revision).IsEqualTo(ReplacedRevision);
        await Assert.That(replaced.Json).IsEqualTo("{\"value\":\"replaced\"}");
        database.Commit(new PatchDocument(Collection, DocumentId,
            [new("/value", PatchKind.Set, "\"patched\"")], ReplacedRevision));
        var patched = database.Database.GetDocument(Root, reference)!;
        await Assert.That(patched.Revision).IsEqualTo(PatchedRevision);
        await Assert.That(patched.Json).IsEqualTo("{\"value\":\"patched\"}");

        database.Commit(new DeleteDocument(Collection, DocumentId, PatchedRevision));
        var tombstone = DocumentCrudFixture.ReadRecord(database, reference)!;
        await Assert.That(tombstone.Revision).IsEqualTo(TombstoneRevision);
        await Assert.That(tombstone.Deleted).IsTrue();
        await Assert.That(tombstone.Json).IsEqualTo("{}");
        await Assert.That(database.Database.GetDocument(Root, reference)).IsNull();

        database.Commit(new PutDocument(Collection, DocumentId, "{\"value\":\"recreated\"}", TombstoneRevision));
        var recreated = database.Database.GetDocument(Root, reference);
        await Assert.That(recreated!.Revision).IsEqualTo(RecreatedRevision);
        await Assert.That(recreated.Json).IsEqualTo("{\"value\":\"recreated\"}");
    }

    [Test]
    public async Task AcDstore001MissingStaleTombstoneAndEventAuthorityFailuresKeepRecords()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, DocumentId, "{\"value\":\"initial\"}", 0));
        var reference = new EntityRef(database.Partition, Collection, DocumentId);
        var initial = DocumentCrudFixture.ReadRecord(database, reference)!;
        var invalidJson = DocumentCrudFixture.Submit(database,
            new PutDocument(Collection, "order-invalid", "{not-json"));
        var stalePatch = DocumentCrudFixture.Submit(database,
            new PatchDocument(Collection, DocumentId, [new("/value", PatchKind.Set, "\"stale\"")], 0));
        var missingPatch = DocumentCrudFixture.Submit(database,
            new PatchDocument(Collection, "missing", [new("/value", PatchKind.Set, "1")], 1));
        var missingDelete = DocumentCrudFixture.Submit(database, new DeleteDocument(Collection, "missing", 1));

        await Assert.That(invalidJson.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(stalePatch.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(missingPatch.Error).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(missingDelete.Error).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)).IsEqualTo(initial);

        database.Commit(new DeleteDocument(Collection, DocumentId, FirstRevision));
        var tombstone = DocumentCrudFixture.ReadRecord(database, reference)!;
        var patchTombstone = DocumentCrudFixture.Submit(database,
            new PatchDocument(Collection, DocumentId, [new("/value", PatchKind.Set, "1")], SingleDeleteRevision));
        var deleteTombstone = DocumentCrudFixture.Submit(database,
            new DeleteDocument(Collection, DocumentId, SingleDeleteRevision));
        await Assert.That(patchTombstone.Error).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(deleteTombstone.Error).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)).IsEqualTo(tombstone);

        DocumentCrudFixture.ConfigureAuthority(database, "event-owned", DocumentAuthority.EventStream);
        var eventPut = DocumentCrudFixture.Submit(database,
            new PutDocument("event-owned", DocumentId, "{}"));
        var eventPatch = DocumentCrudFixture.Submit(database,
            new PatchDocument("event-owned", DocumentId, [new("/value", PatchKind.Set, "1")], 1));
        var eventDelete = DocumentCrudFixture.Submit(database,
            new DeleteDocument("event-owned", DocumentId, 1));
        await Assert.That(eventPut.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(eventPatch.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(eventDelete.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, "event-owned", DocumentId))).IsNull();
    }
}
