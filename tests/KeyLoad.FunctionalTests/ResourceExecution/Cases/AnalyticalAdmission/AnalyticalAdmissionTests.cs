using KeyLoad.FunctionalTests.QueryExecution;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class AnalyticalAdmissionTests
{
    private const string Collection = "orders";
    private const string Principal = "root";
    private const string Sql = "SELECT TOP (1) * FROM orders";
    private const string InvalidSql = "invalid SQL";
    private const string SearchTerm = "needle";
    private const string SearchField = "/text";
    private const string FirstDocument = "a";
    private const string SecondDocument = "b";
    private const string DocumentJson = "{\"text\":\"needle\"}";
    private const int ConcurrentReservations = 256;

    [Test]
    public async Task AcMp004_ConcurrentReservationsAreCountedAndNeverRefused()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        var zero = Assert.ThrowsExactly<InvalidOperationException>(() => _ = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueueTransferAcceptAttempts = 0 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution()));
        await Assert.That(zero.Message).IsEqualTo(DatabaseLimits.ValidationMessage);
        var reservations = new List<IDisposable>();
        try
        {
            for (var index = 0; index < ConcurrentReservations; index++)
            {
                reservations.Add(db.Database.AdmitQuery(default));
            }
            await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(ConcurrentReservations);
            await Assert.That(new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()).Execute(Principal,
                new(db.Partition, Sql, AllowFullScan: true)).Rows).IsEmpty();
            await Assert.That(await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Principal,
                new(db.Partition, Collection, SearchField, SearchTerm))).IsEmpty();
            await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(ConcurrentReservations);
        }
        finally
        {
            foreach (var reservation in reservations)
            {
                reservation.Dispose();
                reservation.Dispose();
            }
        }
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
    }

    [Test]
    public async Task AcMp012_CancellationAndValidationFailuresReleaseAdmission()
    {
        using var db = new TestDatabase();
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
    public async Task AcMp012_InFlightCancellationAfterNativeReadProgressReleasesAdmission()
    {
        var clock = new QueryObservedWorkClock();
        using var db = new TestDatabase(timeProvider: clock);
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, FirstDocument, DocumentJson),
            new PutDocument(Collection, SecondDocument, DocumentJson));
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(db.Partition, Collection, SearchField, SearchTerm);
        using var cancellation = new CancellationTokenSource();
        var before = db.Store.GetReadDiagnostics();
        var position = db.Store.Position;
        var admitted = 0;
        clock.Arm(() => db.Store.GetReadDiagnostics().RangeExaminedBytes > before.RangeExaminedBytes, () =>
        {
            admitted = db.Database.QueryReadsInFlight;
            cancellation.Cancel();
        });
        RankedDocument[]? partial = null;
        try
        {
            var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                partial = search.Search(Principal, request, cancellation.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        }
        finally
        {
            clock.Disarm();
        }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(admitted).IsEqualTo(1);
        await Assert.That(db.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(before.RangeExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        var healthy = await search.SearchAsync(Principal, request);
        await Assert.That(string.Join(',', healthy.Select(row => row.Document.Reference.Id)))
            .IsEqualTo(FirstDocument + "," + SecondDocument);
        await Assert.That(healthy.All(row => row.Document.Json == DocumentJson && row.Document.Revision == 1
            && !row.Document.Redacted && row.Document.Reference == db.ExpectedReference(
                new(db.Partition, Collection, row.Document.Reference.Id)))).IsTrue();
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }

}
