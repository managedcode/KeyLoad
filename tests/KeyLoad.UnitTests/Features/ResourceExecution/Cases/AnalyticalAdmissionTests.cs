using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AnalyticalAdmissionTests
{
    private const string Collection = "orders";
    private const string Principal = "root";
    private const string Sql = "SELECT * FROM orders LIMIT 1";
    private const string InvalidSql = "invalid SQL";
    private const string SearchTerm = "needle";
    private const string SearchField = "/text";
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AcMp004_ReservationIsSharedExactlyOnceAndRejectsInvalidLimits()
    {
        using var db = new TestDatabase(new() { MaxConcurrentQueries = 1 });
        var zero = Assert.ThrowsExactly<InvalidOperationException>(() => _ = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxConcurrentQueries = 0 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution()));
        var negative = Assert.ThrowsExactly<InvalidOperationException>(() => _ = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxConcurrentQueries = -1 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution()));
        await Assert.That(zero.Message).IsEqualTo(DatabaseLimits.ValidationMessage);
        await Assert.That(negative.Message).IsEqualTo(DatabaseLimits.ValidationMessage);
        using var reservation = db.Database.AdmitQuery(default);
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(1);
        var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.AdmitQuery(default));
        await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        reservation.Dispose();
        reservation.Dispose();
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        using var next = db.Database.AdmitQuery(default);
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(1);
    }

    [Test]
    public async Task AcMp004_RealStoreWaitSharesOneAdmissionAcrossAllAnalyticalEntries()
    {
        using var db = new TestDatabase(new() { MaxConcurrentQueries = 1 });
        using var independent = new TestDatabase(new() { MaxConcurrentQueries = 1 });
        db.Configure(Collection, ResourceKind.Collection);
        independent.Configure(Collection, ResourceKind.Collection);
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(db.Partition, Collection, SearchField, SearchTerm);
        var tasks = new AnalyticalAdmissionTaskLifetime(CoordinationTimeout);
        Task<RankedDocument[]>? admitted = null;
        await using var held = new RealZoneTreeWriteGateHold(db.Store);
        await held.WaitUntilEnteredAsync();
        await tasks.RunWithCleanupAsync(async () =>
        {
            admitted = tasks.StartWorker(() => search.Search(Principal, request));
            var admissionWait = tasks.StartAdmissionWait(db.Database);
            await Assert.That(await admissionWait.WaitAsync(CoordinationTimeout)).IsTrue();
            await AssertSaturatedRequests(db, independent, tasks);
        }, static () => Task.CompletedTask, held.ReleaseAsync,
            () => tasks.AssertWorkerCompletesAsync(admitted!));
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()).Execute(Principal,
            new(db.Partition, Sql, AllowFullScan: true)).Rows).IsEmpty();
    }

    [Test]
    public async Task AcMp012_CancellationAndValidationFailuresReleaseAdmission()
    {
        using var db = new TestDatabase(new() { MaxConcurrentQueries = 1 });
        db.Configure(Collection, ResourceKind.Collection);
        var query = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => db.Database.AdmitQuery(cancelled.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => search.Search(Principal,
            new(db.Partition, Collection, SearchField, SearchTerm), cancelled.Token));
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        Assert.ThrowsExactly<KeyLoadException>(() => query.Execute(Principal,
            new(db.Partition, InvalidSql, AllowFullScan: true)));
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(await search.SearchAsync(Principal,
            new(db.Partition, Collection, SearchField, SearchTerm))).IsEmpty();
    }

    [Test]
    public async Task AcMp012_InFlightCancellationAtRealStoreGateReleasesAdmission()
    {
        using var db = new TestDatabase(new() { MaxConcurrentQueries = 1 });
        db.Configure(Collection, ResourceKind.Collection);
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(db.Partition, Collection, SearchField, SearchTerm);
        using var cancellation = new CancellationTokenSource();
        var tasks = new AnalyticalAdmissionTaskLifetime(CoordinationTimeout);
        Task<RankedDocument[]>? admitted = null;
        await using var held = new RealZoneTreeWriteGateHold(db.Store);
        await held.WaitUntilEnteredAsync();
        await tasks.RunWithCleanupAsync(async () =>
        {
            admitted = tasks.StartExpectedCancellation(() => search.Search(Principal, request, cancellation.Token));
            var admissionWait = tasks.StartAdmissionWait(db.Database);
            await Assert.That(await admissionWait.WaitAsync(CoordinationTimeout)).IsTrue();
        }, cancellation.CancelAsync, held.ReleaseAsync,
            () => tasks.AssertWorkerIsCancelledAsync(admitted!));
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(await search.SearchAsync(Principal, request)).IsEmpty();
    }

    [Test]
    public async Task AcMp012_ReadBudgetFailureReleasesAllowanceForFollowingSearch()
    {
        using var db = new TestDatabase(new() { MaxConcurrentQueries = 1, MaxQueryReadBytes = 1 });
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure("empty", ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "hit", "{\"text\":\"needle\"}"));
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => search.Search(Principal,
            new(db.Partition, Collection, SearchField, SearchTerm)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(await search.SearchAsync(Principal,
            new(db.Partition, "empty", SearchField, SearchTerm))).IsEmpty();
    }

    private static async Task AssertSaturatedRequests(TestDatabase db, TestDatabase independent,
        AnalyticalAdmissionTaskLifetime tasks)
    {
        var firstQuery = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var secondQuery = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var sql = new QueryRequest(db.Partition, InvalidSql, AllowFullScan: true);
        var ast = new AstQueryRequest(db.Partition,
            new SelectQuery(Collection, null, [new("*", "document")], null, [], 1), AllowFullScan: true);
        var request = new SearchRequest(db.Partition, Collection, SearchField, SearchTerm);
        var before = db.Store.GetReadDiagnostics();
        var rejections = tasks.StartRejectedRequests(
            () => firstQuery.Execute(Principal, sql),
            () => secondQuery.Execute(Principal, sql),
            () => secondQuery.ExecuteAst(Principal, ast),
            () => firstQuery.StartLiveQuery(Principal, new(ast)),
            () => secondQuery.ReadLiveQuery(Principal, new(ast, "invalid")),
            () => search.Search(Principal, request));
        await tasks.AssertRejectedAsync(rejections);
        var after = db.Store.GetReadDiagnostics();
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(after.OwnedPointLookups - before.OwnedPointLookups).IsEqualTo(0L);
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(0L);
        await Assert.That(after.RangeVisitAttempts - before.RangeVisitAttempts).IsEqualTo(0L);
        await Assert.That(await new SearchEngine(independent.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Principal,
            new(independent.Partition, Collection, SearchField, SearchTerm))).IsEmpty();
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(1);
        await Assert.That(independent.Database.QueryReadsInFlight).IsEqualTo(0);
    }

}
