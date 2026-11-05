namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentIdentifierRangeTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string Staged = "before-identifier";
    private const string Healthy = "after-identifier";
    private const string Json = "{\"value\":\"identifier\"}";
    private const int WorldCodePoint = 0x1F30D;

    [Test]
    [Arguments(0x0000, true)]
    [Arguments(0x001f, true)]
    [Arguments(0x007f, true)]
    [Arguments(0x0080, true)]
    [Arguments(0x009f, true)]
    [Arguments(0x0020, false)]
    [Arguments(0x007e, false)]
    [Arguments(0x00a0, false)]
    [Arguments(WorldCodePoint, false)]
    public async Task AcDstore001IdentifierControlRangesPreserveAtomicityAndExactUnicode(int codePoint, bool denied)
    {
        var id = codePoint == WorldCodePoint ? "Київ" + char.ConvertFromUtf32(codePoint)
            : string.Concat("a", char.ConvertFromUtf32(codePoint), "b");
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
