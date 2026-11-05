using System.Globalization;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentBatchResourceReadTests
{
    private const string Collection = DocumentCrudFixture.Collection;
    private const string Json = "{\"value\":\"batched\"}";
    private const int SmallerBatch = 4;
    private const int LargerBatch = 64;

    [Test]
    public async Task AcDstore005BatchMetadataReadsStayConstantAsDocumentCountGrows()
    {
        var smaller = await CommitAndReadAsync(SmallerBatch);
        var larger = await CommitAndReadAsync(LargerBatch);
        await Assert.That(larger - smaller).IsEqualTo(LargerBatch - SmallerBatch);
    }

    private static async Task<long> CommitAndReadAsync(int count)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
            [.. Enumerable.Range(0, count).Select(ordinal => (Mutation)new PutDocument(Collection,
                ordinal.ToString(CultureInfo.InvariantCulture), Json, ExpectedRevision: 0))]);
        var before = database.Store.GetReadDiagnostics();
        var receipt = database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        var after = database.Store.GetReadDiagnostics();
        var points = after.BorrowedPointLookups - before.BorrowedPointLookups;
        await Assert.That(receipt.Mutations.Length).IsEqualTo(count);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var reference = new EntityRef(database.Partition, Collection, ordinal.ToString(CultureInfo.InvariantCulture));
            var actual = database.Database.GetDocument(DocumentCrudFixture.Root, reference);
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Json).IsEqualTo(Json);
            await Assert.That(actual.Revision).IsEqualTo(1L);
        }
        var retry = database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(retry).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        var head = database.Store.Read(view => database.Database.ReadOutboxHead(view, database.Partition));
        await Assert.That(head.Tail).IsEqualTo((long)count);
        await Assert.That(head.StoredRecords).IsEqualTo((long)count);
        return points;
    }
}
