namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentRowTenantMutationAuthorizationTests
{
    private const string Root = DocumentCrudFixture.Root;
    private const string Collection = "rows";
    private const string OwnerPrincipal = "alice";
    private const string OtherPrincipal = "bob";
    private const string OwnerAlice = "owner-alice";
    private const string OwnerBob = "owner-bob";
    private const string ForeignTenant = "foreign-tenant";
    private const string EmptyObject = "{}";
    private const int FirstRevision = 1;
    private const int SecondRevision = 2;
    private const int ThirdRevision = 3;

    [Test]
    public async Task AcDstore003RowOwnerAndTenantCannotBeForgedByPutPatchOrDelete()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "alice-row", EmptyObject, Access: new(OwnerAlice)),
            new PutDocument(Collection, "bob-row", EmptyObject, Access: new(OwnerBob)));
        DocumentCrudFixture.ConfigurePrincipal(database, OwnerPrincipal, Collection,
            Capability.DocumentsWrite, [], OwnerAlice, restrictRows: true);
        DocumentCrudFixture.ConfigurePrincipal(database, OtherPrincipal, Collection,
            Capability.DocumentsWrite, [], OwnerBob, restrictRows: true);

        var reference = new EntityRef(database.Partition, Collection, "alice-row");
        var original = DocumentCrudFixture.ReadRecord(database, reference)!;
        var deniedPatch = DocumentCrudFixture.Submit(database, OtherPrincipal,
            new PatchDocument(Collection, "alice-row", [new("/value", PatchKind.Set, "1")], FirstRevision));
        var deniedReplacement = DocumentCrudFixture.Submit(database, OtherPrincipal,
            new PutDocument(Collection, "alice-row", EmptyObject, FirstRevision, new(OwnerBob), ExplicitReplacement: true));
        var deniedDelete = DocumentCrudFixture.Submit(database, OtherPrincipal,
            new DeleteDocument(Collection, "alice-row", FirstRevision));
        var forgedOwner = DocumentCrudFixture.Submit(database, OwnerPrincipal,
            new PutDocument(Collection, "forged-owner", EmptyObject, 0, new(OwnerBob)));
        await Assert.That(deniedPatch.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(deniedReplacement.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(deniedDelete.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(forgedOwner.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)).IsEqualTo(original);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(database.Partition, Collection, "forged-owner"))).IsNull();

        var foreignPartition = database.Partition with { TenantId = ForeignTenant };
        var foreignWrite = DocumentCrudFixture.SubmitAt(database, OwnerPrincipal, foreignPartition,
            new PutDocument(Collection, "forged-tenant", EmptyObject, 0));
        await Assert.That(foreignWrite.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(DocumentCrudFixture.ReadRecord(database,
            new(foreignPartition, Collection, "forged-tenant"))).IsNull();

        var ownerPatch = DocumentCrudFixture.Submit(database, OwnerPrincipal,
            new PatchDocument(Collection, "alice-row", [new("/value", PatchKind.Set, "1")], FirstRevision));
        await Assert.That(ownerPatch.Error).IsNull();
        await Assert.That(DocumentCrudFixture.ReadRecord(database, reference)!.Revision).IsEqualTo(SecondRevision);
        var ownerDelete = DocumentCrudFixture.Submit(database, OwnerPrincipal,
            new DeleteDocument(Collection, "alice-row", SecondRevision));
        await Assert.That(ownerDelete.Error).IsNull();
        var tombstone = DocumentCrudFixture.ReadRecord(database, reference)!;
        await Assert.That(tombstone.Revision).IsEqualTo(ThirdRevision);
        await Assert.That(tombstone.Deleted).IsTrue();

        await Assert.That(database.Database.GetDocument(Root, reference)).IsNull();
    }
}
