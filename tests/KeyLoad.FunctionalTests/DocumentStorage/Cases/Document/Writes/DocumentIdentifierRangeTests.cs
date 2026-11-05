namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentIdentifierRangeTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string Staged = "before-identifier";
    private const string Healthy = "after-identifier";
    private const string Json = "{\"value\":\"identifier\"}";

    [Test]
    [Arguments("a\u0000b", true)]
    [Arguments("a\u001fb", true)]
    [Arguments("a\u007fb", true)]
    [Arguments("a\u0080b", true)]
    [Arguments("a\u009fb", true)]
    [Arguments("a b", false)]
    [Arguments("a~b", false)]
    [Arguments("a\u00a0b", false)]
    [Arguments("Київ🌍", false)]
    public async Task AcDstore001IdentifierControlRangesPreserveAtomicityAndExactUnicode(string id, bool denied)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var result = DocumentCrudFixture.Submit(database,
            new PutDocument(Collection, Staged, Json, ExpectedRevision: 0),
            new PutDocument(Collection, id, Json, ExpectedRevision: 0));
        if (denied)
        {
            await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.Validation);
            await Assert.That(database.Database.GetDocument(DocumentCrudFixture.Root,
                new(database.Partition, Collection, Staged))).IsNull();
        }
        else
        {
            await Assert.That(result.Get<CommitReceipt>().Mutations.Length).IsEqualTo(2);
            var actual = database.Database.GetDocument(DocumentCrudFixture.Root,
                new(database.Partition, Collection, id));
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Reference.Id).IsEqualTo(id);
            await Assert.That(actual.Json).IsEqualTo(Json);
        }
        var healthy = DocumentCrudFixture.Submit(database,
            new PutDocument(Collection, Healthy, Json, ExpectedRevision: 0)).Get<CommitReceipt>();
        await Assert.That(healthy.Mutations.Length).IsEqualTo(1);
    }
}
