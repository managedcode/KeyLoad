using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphSearchResultCompositionTests
{
    private const int ResultCap = 4_096;
    private const int PayloadLength = 2_200;
    private const int SnapshotCapacity = 512;
    private const string Principal = "root";

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task MismatchedSqlAndSearchOwnerCapsRejectBeforeFinalGraphSerializationAndHealthyGraphPreservesFullState(bool sqlIsLower)
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var json = JsonSerializer.Serialize(new { text = new string('x', PayloadLength) });
        database.Commit(new PutDocument(GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit, json, 1),
            new PutDocument(GraphSearchTestSupport.Documents, GraphSearchTestSupport.SecondHit, json, 1));
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = SqlGraphSearchTestSupport.Request(database.Partition, SqlGraphSearchTestSupport.RetrieverSql());
        var baseline = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .SearchSqlAsync(Principal, request, token);
        await AssertHitsAsync(database, baseline, json, false);
        await Assert.That(JsonDefaults.Serialize(baseline).Length).IsGreaterThan(ResultCap);
        var before = CaptureState(database);
        await Assert.That(before.HasMore).IsFalse();
        var position = database.Store.Position;
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(new QueryExecutionOptions
        { MaximumResultBytes = sqlIsLower ? null : ResultCap }));
        var owner = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution(new QueryExecutionOptions
        { MaximumResultBytes = sqlIsLower ? ResultCap : null }), search);
        GraphSearchResult? partial = null;
        KeyLoadException? failure = null;
        try
        { partial = await owner.SearchSqlAsync(Principal, request, token); }
        catch (KeyLoadException error) { failure = error; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        // This cumulative selected-document error precedes GraphSearchResult allocation/serialization.
        await Assert.That(failure!.Message).IsEqualTo("The search result byte budget is exceeded.");
        await Assert.That(partial).IsNull();
        await AssertUnchangedAsync(database, before, position);
        var healthy = await owner.SearchSqlAsync(Principal, SqlGraphSearchTestSupport.Request(database.Partition,
            SqlGraphSearchTestSupport.RetrieverSql(allowedIds: "'a-hit'")), token);
        await AssertHitsAsync(database, healthy, json, true);
        await AssertUnchangedAsync(database, before, position);
    }

    private static ScanPage CaptureState(TestDatabase database)
        => database.Store.Read(view => view.Scan([], SnapshotCapacity));

    private static async Task AssertUnchangedAsync(TestDatabase database, ScanPage before, long position)
    {
        var after = CaptureState(database);
        await Assert.That(after.HasMore).IsFalse();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(after).AsSpan().SequenceEqual(JsonDefaults.Serialize(before))).IsTrue();
    }

    private static async Task AssertHitsAsync(TestDatabase database, GraphSearchResult result, string json, bool single)
    {
        var ids = single ? new[] { "a-hit" } : new[] { "a-hit", "b-hit" };
        await Assert.That(result.Hits.Select(hit => hit.Document.Reference)).IsEquivalentTo(ids.Select(id =>
            new EntityRef(database.Partition, "graph-search-documents", id)), CollectionOrdering.Matching);
        await Assert.That(result.Hits.Select(hit => hit.Document.Json)).IsEquivalentTo(
            ids.Select(_ => json), CollectionOrdering.Matching);
        await Assert.That(result.Hits.All(hit => hit.Document.Revision == 2 && !hit.Document.Redacted
            && hit.Document.RedactedFields.IsEmpty)).IsTrue();
        await Assert.That(result.Hits.Select(hit => hit.Score)).IsEquivalentTo(single
            ? new[] { 1d / 61 } : new[] { 1d / 61, 1d / 62 }, CollectionOrdering.Matching);
        await Assert.That(result.Expansion).IsNull();
    }
}
