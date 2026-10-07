using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryObservedWorkBudgetTests
{
    private const int DeadlineSeconds = 1;
    private const int ExpiredSeconds = 2;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";
    private const string IndexSql = "SELECT * FROM orders WHERE status = 'open' ORDER BY id";
    private const string ScanSql = "SELECT * FROM orders ORDER BY id";
    private const string Principal = EqualityAccessPathSupport.Root;

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task AcMp003ObservedNativeQueryWorkRejectsWithoutPartialPageThenReturnsCompleteHealthyPage(
        bool typed, bool indexed, bool cancel)
    {
        var clock = new QueryObservedWorkClock();
        using var database = new TestDatabase(new DatabaseLimits { QueryDeadlineSeconds = DeadlineSeconds }, timeProvider: clock);
        database.Configure(EqualityAccessPathSupport.Collection, ResourceKind.Collection,
            indexes: [new(EqualityAccessPathSupport.StatusIndex, [EqualityAccessPathSupport.StatusPath])]);
        database.Commit(new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.PrimaryId,
                EqualityAccessPathSupport.PrimaryJson),
            new PutDocument(EqualityAccessPathSupport.Collection, EqualityAccessPathSupport.SecondaryId,
                EqualityAccessPathSupport.SecondaryJson));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var sql = indexed ? IndexSql : ScanSql;
        var request = new QueryRequest(database.Partition, sql, AllowFullScan: !indexed);
        var query = new SqlParser(sql, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            UnitExecutionOptions.QueryExecution()).Parse();
        var ast = QueryAdapterTestSupport.RoundTrip(new(database.Partition, query, AllowFullScan: !indexed));
        using var cancellation = new CancellationTokenSource();
        QueryPage Execute() => typed ? engine.ExecuteAst(Principal, ast, clock, cancellationToken: cancellation.Token)
            : engine.Execute(Principal, request, clock, cancellationToken: cancellation.Token);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes,
            () => { if (cancel) { cancellation.Cancel(); } else { clock.Advance(TimeSpan.FromSeconds(ExpiredSeconds)); } });
        QueryPage? partial = null;
        try
        {
            if (cancel)
            {
                var error = Assert.ThrowsExactly<OperationCanceledException>(() => partial = Execute());
                await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
            }
            else
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => partial = Execute());
                await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                await Assert.That(error.Message).IsEqualTo(DeadlineDetail);
            }
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before.RangeExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = typed ? engine.ExecuteAst(Principal, ast, clock) : engine.Execute(Principal, request, clock);
        await AssertHealthyAsync(healthy, indexed, position);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    private static async Task AssertHealthyAsync(QueryPage healthy, bool indexed, long position)
    {
        var expected = new QueryRow[] {
            new(EqualityAccessPathSupport.PrimaryId, 1, EqualityAccessPathSupport.PrimaryJson, RedactedFields: []),
            new(EqualityAccessPathSupport.SecondaryId, 1, EqualityAccessPathSupport.SecondaryJson, RedactedFields: []) };
        await Assert.That(JsonDefaults.Serialize(healthy.Rows).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(healthy.Cursor).IsNull();
        await Assert.That(healthy.CutPosition).IsEqualTo(position);
        await Assert.That(healthy.AccessPath).IsEqualTo(indexed ? EqualityAccessPathSupport.IndexPath : EqualityAccessPathSupport.FullScanPath);
    }
}
