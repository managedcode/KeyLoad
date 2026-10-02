using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class SearchResourceTests
{
    private const string Collection = "orders";
    private const string SmallCollection = "small-search";
    private const string LargeCollection = "large-search";
    private const string TextPath = "/text";
    private const string VectorPath = "/embedding";
    private const string SearchTerm = "needle";
    private const string HitId = "hit";
    private const int CorpusDocuments = 64;
    private const int PaddingCharacters = 16_384;
    private const int MeasurementPairs = 3;
    private const long FixedAllocationAllowance = 131_072;
    private const double ScoreTolerance = 0.0000000000005;
    private static VectorSpace Space { get; } = new("search-resource", 2, DistanceMetric.DotProduct, "test", "1");

    [Test]
    public async Task AcMp003LargeNonmatchingPayloadsDoNotAllocateRawCorpusCopies()
    {
        using var db = new TestDatabase();
        db.Configure(SmallCollection, ResourceKind.Collection);
        db.Configure(LargeCollection, ResourceKind.Collection);
        var padding = new string('x', PaddingCharacters);
        var evaluatedAt = TimeProvider.System.GetUtcNow();
        CommitAt(db, evaluatedAt, Rows(SmallCollection, string.Empty));
        CommitAt(db, evaluatedAt, Rows(LargeCollection, padding));
        await AssertStoredTimesAsync(db, SmallCollection, evaluatedAt);
        await AssertStoredTimesAsync(db, LargeCollection, evaluatedAt);
        var addedStoredBytes = StoredBytes(LargeCollection) - StoredBytes(SmallCollection);
        var search = new SearchEngine(db.Database);
        var small = new SearchRequest(db.Partition, SmallCollection, TextPath, SearchTerm, Limit: 1);
        var large = new SearchRequest(db.Partition, LargeCollection, TextPath, SearchTerm, Limit: 1);
        var token = TestContext.Current!.Execution.CancellationToken;
        for (var index = 0; index < MeasurementPairs; index++)
        {
            search.Search("root", small, token);
            search.Search("root", large, token);
        }

        long minimumSmall = long.MaxValue, minimumLarge = long.MaxValue;
        RankedDocument[] largeResult = [];
        for (var index = 0; index < MeasurementPairs; index++)
        {
            minimumSmall = Math.Min(minimumSmall, Measure(small, out _));
            minimumLarge = Math.Min(minimumLarge, Measure(large, out largeResult));
        }
        var extraAllocation = minimumLarge - minimumSmall;
        // UTF-16 decoded JSON costs about twice the added stored ASCII bytes. The half-corpus
        // margin covers parser/provider metadata; an owned raw scan page adds another full copy.
        var allowance = 2 * addedStoredBytes + addedStoredBytes / 2 + FixedAllocationAllowance;
        await Assert.That(addedStoredBytes).IsEqualTo((long)CorpusDocuments * PaddingCharacters);
        await Assert.That(extraAllocation).IsGreaterThan(0L);
        await Assert.That(extraAllocation).IsLessThan(allowance);
        var result = await Assert.That(largeResult).HasSingleItem();
        await Assert.That(result.Document.Reference.Id).IsEqualTo(HitId);
        await Assert.That(result.Document.Json).IsEqualTo("{\"text\":\"needle\"}");
        await Assert.That(result.Score).IsEqualTo(1.0 / 61).Within(ScoreTolerance);

        long StoredBytes(string collection) => db.Store.Read(view => view.Scan(
            DocumentStorageKeys.Prefix(db.Partition, collection), CorpusDocuments + 1).Records
            .Sum(record => (long)record.Value.Length));
        long Measure(SearchRequest request, out RankedDocument[] result)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            result = search.Search("root", request, token);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private static Mutation[] Rows(string collection, string extra) => Enumerable.Range(0, CorpusDocuments)
        .Select(index => (Mutation)new PutDocument(collection, $"row-{index:D2}",
            "{\"text\":\"other\",\"padding\":\"" + extra + "\"}"))
        .Append(new PutDocument(collection, HitId, "{\"text\":\"needle\"}"))
        .ToArray();

    private static void CommitAt(TestDatabase db, DateTimeOffset time, Mutation[] mutations)
    {
        var commandId = Guid.NewGuid();
        db.Submit(OperationKind.Batch, new CommandRequest(commandId, db.Partition, [.. mutations]), id: commandId, time: time)
            .Get<CommitReceipt>();
    }

    private static async Task AssertStoredTimesAsync(TestDatabase db, string collection, DateTimeOffset evaluatedAt)
    {
        var updatedAt = db.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(db.Partition, collection),
            CorpusDocuments + 1).Records
            .Select(record => JsonDefaults.Deserialize<DocumentRecord>(record.Value.Span).UpdatedAt).ToArray());
        await Assert.That(updatedAt.Length).IsEqualTo(CorpusDocuments + 1);
        await Assert.That(updatedAt.All(time => time == evaluatedAt)).IsTrue();
    }

    [Test]
    public async Task AcMp005ExactSearchEnvelopeBoundaryAndFollowingRead()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, HitId, "{\"text\":\"needle\"}"));
        var request = new SearchRequest(db.Partition, Collection, TextPath, SearchTerm);
        var expected = new SearchEngine(db.Database).Search("root", request);
        var exactBytes = JsonDefaults.Serialize(expected).Length;
        var exact = new SearchEngine(new DatabaseEngine(db.Store, db.Database.Authorization,
            new() { MaxBatchBytes = exactBytes }));
        var shortBudget = new SearchEngine(new DatabaseEngine(db.Store, db.Database.Authorization,
            new() { MaxBatchBytes = exactBytes - 1 }));
        var position = db.Store.Position;

        var accepted = exact.Search("root", request, TestContext.Current!.Execution.CancellationToken);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => shortBudget.Search("root", request,
            TestContext.Current!.Execution.CancellationToken));

        await Assert.That(JsonDefaults.Serialize(accepted))
            .IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(exact.Search("root", request, TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
    }

    [Test]
    public async Task AcMp004MidScanTextWorkRejectionReleasesReadGate()
    {
        using var db = new TestDatabase(new() { MaxSearchTextTokens = 2 });
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, HitId, "{\"text\":\"needle other\"}"),
            new PutVector(Collection, HitId, VectorPath, [1, 0], Space, 1));
        var search = new SearchEngine(db.Database);
        var position = db.Store.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => search.Search("root",
            new(db.Partition, Collection, TextPath, SearchTerm), TestContext.Current!.Execution.CancellationToken));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        var healthy = await Assert.That(search.Search("root", new(db.Partition, Collection,
            VectorField: VectorPath, Vector: [1, 0], Space: Space),
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(healthy.Document.Reference.Id).IsEqualTo(HitId);
    }
}
