using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryObservedWorkBudgetTests
{
    private const string IndexSql = "SELECT * FROM orders WHERE status = 'open' ORDER BY id";
    private const string ScanSql = "SELECT * FROM orders ORDER BY id";
    private const string Principal = EqualityAccessPathSupport.Root;
    private const int PaddingCharacters = 262_144;
    private static readonly string PrimaryJson = Padded(EqualityAccessPathSupport.PrimaryJson);
    private static readonly string SecondaryJson = Padded(EqualityAccessPathSupport.SecondaryJson);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AcMp003ObservedQueryWorkRejectsWithoutPartialPageThenReturnsCompleteHealthyPage(
        bool typed, bool indexed)
    {
        using var database = new TestDatabase();
        database.Configure(EqualityAccessPathSupport.Collection, ResourceKind.Collection,
            indexes: [new(EqualityAccessPathSupport.StatusIndex, [EqualityAccessPathSupport.StatusPath])]);
        database.Commit(new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.PrimaryId,
                PrimaryJson),
            new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.SecondaryId,
                SecondaryJson));
        var sql = indexed ? IndexSql : ScanSql;
        var request = new QueryRequest(database.Partition, sql, AllowFullScan: !indexed);
        var query = new SqlParser(sql,
            UnitExecutionOptions.QueryExecution()).Parse();
        var ast = QueryAdapterTestSupport.RoundTrip(new(database.Partition, query, AllowFullScan: !indexed));
        using var cancellation = new CancellationTokenSource();
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        var triggered = false;
        using var observed = new ReadObservedStore(database.Store, afterRange: () =>
        {
            if (!triggered && database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes)
            {
                triggered = true;
                cancellation.Cancel();
            }
        });
        var engine = new QueryEngine(database.CreateConfiguredDatabase(observed), UnitExecutionOptions.QueryExecution());
        QueryPage Execute() => typed ? engine.ExecuteAst(Principal, ast, cancellationToken: cancellation.Token)
            : engine.Execute(Principal, request, cancellationToken: cancellation.Token);
        QueryPage? partial = null;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => partial = Execute());
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(triggered).IsTrue();
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before.RangeExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = typed ? engine.ExecuteAst(Principal, ast) : engine.Execute(Principal, request);
        await AssertHealthyAsync(healthy, indexed, position);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    private static string Padded(string json)
        => json[..^1] + ",\"padding\":\"" + new string('x', PaddingCharacters) + "\"}";

    private static async Task AssertHealthyAsync(QueryPage healthy, bool indexed, long position)
    {
        var expected = new QueryRow[] {
            new(EqualityAccessPathSupport.PrimaryId, 1, PrimaryJson, RedactedFields: []),
            new(EqualityAccessPathSupport.SecondaryId, 1, SecondaryJson, RedactedFields: []) };
        await Assert.That(JsonDefaults.Serialize(healthy.Rows).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(healthy.Cursor).IsNull();
        await Assert.That(healthy.CutPosition).IsEqualTo(position);
        await Assert.That(healthy.AccessPath).IsEqualTo(indexed ? EqualityAccessPathSupport.IndexPath : EqualityAccessPathSupport.FullScanPath);
    }
}
