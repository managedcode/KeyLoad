using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal sealed class AnnSeedReadBudgetTests
{
    private const string LineageKeySpace = "vector-projection-lineage";

    [Test]
    public async Task MetadataVectorAndDocumentReadsAreChargedExactlyOnce()
    {
        using var database = CreateVisibilitySource();
        var expectedBytes = MeasureSourceBytes(database);
        var budget = new ReadExecutionBudget();
        var seed = AnnSeedTestSupport.Capture(database, budget: budget);

        await Assert.That(seed.Records.Select(record => record.DocumentId).ToArray())
            .IsEquivalentTo(["visible"], CollectionOrdering.Matching);
        await Assert.That(seed.ReadBytes).IsEqualTo(expectedBytes);
        await Assert.That(budget.ReadBytes).IsEqualTo(expectedBytes);
    }

    private static TestDatabase CreateVisibilitySource()
    {
        var database = AnnSeedTestSupport.Create(0);
        database.Commit(
            new PutDocument(AnnSeedTestSupport.Collection, "visible", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "visible", AnnSeedTestSupport.Field,
                [1, 0, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "hidden", "{}", Access: new("other-owner")),
            new PutVector(AnnSeedTestSupport.Collection, "hidden", AnnSeedTestSupport.Field,
                [0, 1, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "stale", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "stale", AnnSeedTestSupport.Field,
                [0, 0, 1], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "deleted", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "deleted", AnnSeedTestSupport.Field,
                [1, 1, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "other-space", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "other-space", AnnSeedTestSupport.Field,
                [0.5f, 0.5f, 0.5f], AnnSeedTestSupport.Space(model: "different-model"), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "missing-document", "{}",
                Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "missing-document", AnnSeedTestSupport.Field,
                [0.25f, 0.75f, 0.5f], AnnSeedTestSupport.Space(), 1));
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(DocumentStorageKeys.RecordKey(database.Partition, AnnSeedTestSupport.Collection,
                "missing-document"));
            return true;
        });
        database.Commit(new PatchDocument(AnnSeedTestSupport.Collection, "stale",
            [new("/state", PatchKind.Set, "1")], 1));
        database.Commit(new DeleteDocument(AnnSeedTestSupport.Collection, "deleted", 1));
        return database;
    }

    private static long MeasureSourceBytes(TestDatabase database, int? maxScanRecords = null)
        => database.Store.Read(view =>
        {
            var counter = new AnnSeedReadByteCounter();
            CountRead(view, KeySpace.Principal(AnnSeedTestSupport.Principal), counter);
            CountRead(view, KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId,
                AnnSeedTestSupport.Collection), counter);
            CountRead(view, ResourceLifetimeCatalog.Key(database.Partition.TenantId, database.Partition.DatabaseId,
                AnnSeedTestSupport.Collection), counter);
            CountRead(view, KeySpace.Partition("outbox-head", database.Partition), counter);
            CountRead(view, KeySpace.Applied.ToArray(), counter);
            view.VisitRange(KeySpace.Partition("vector", database.Partition, AnnSeedTestSupport.Collection,
                AnnSeedTestSupport.Field), maxScanRecords ?? view.MaximumRangeRecords, (_, value) =>
                {
                    var vector = OrleansSerialization.Deserialize<VectorRecord>(value);
                    CountRead(view, DocumentStorageKeys.RecordKey(database.Partition, AnnSeedTestSupport.Collection,
                        vector.DocumentId), counter);
                    var document = view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(database.Partition,
                        AnnSeedTestSupport.Collection, vector.DocumentId));
                    var principal = view.GetRecord<PrincipalRecord>(KeySpace.Principal(AnnSeedTestSupport.Principal))!;
                    if (document is { Deleted: false } && document.Revision == vector.DocumentRevision
                        && database.Database.Authorization.CanReadRow(principal, document.Access))
                    {
                        CountRead(view, KeySpace.Partition(LineageKeySpace, database.Partition,
                            AnnSeedTestSupport.Collection, vector.Field, vector.DocumentId), counter);
                    }
                    return true;
                }, observer: counter.Add);
            return counter.Total;
        });

    private static void CountRead(IKeyValueView view, byte[] key, AnnSeedReadByteCounter counter)
        => view.ReadValue(key, static _ => { }, counter.Add);

    private sealed class AnnSeedReadByteCounter
    {
        internal long Total
        {
            get; private set;
        }
        internal void Add(long value) => Total = checked(Total + value);
    }

}
