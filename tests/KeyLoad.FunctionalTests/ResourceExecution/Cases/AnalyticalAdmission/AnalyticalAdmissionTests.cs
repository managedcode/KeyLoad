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
    private const int ConcurrentReservations = 256;
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

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
    public async Task AcMp012_InFlightCancellationAtRealStoreGateReleasesAdmission()
    {
        var held = new RealZoneTreeWriteGateHold();
        using var db = new TestDatabase(storeFaultObserver: held.Observer);
        await using var heldLifetime = held;
        db.Configure(Collection, ResourceKind.Collection);
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(db.Partition, Collection, SearchField, SearchTerm);
        using var cancellation = new CancellationTokenSource();
        var tasks = new AnalyticalAdmissionTaskLifetime(CoordinationTimeout);
        Task<RankedDocument[]>? admitted = null;
        held.Start(db.Store);
        await held.WaitUntilEnteredAsync();
        await tasks.RunWithCleanupAsync(async () =>
        {
            admitted = tasks.StartExpectedCancellation(() => search.Search(Principal, request, cancellation.Token));
            var admissionWait = tasks.StartAdmissionWait(db.Database);
            await Assert.That(await admissionWait.WaitAsync(CoordinationTimeout, TimeProvider.System)).IsTrue();
        }, cancellation.CancelAsync, held.ReleaseAsync,
            () => tasks.AssertWorkerIsCancelledAsync(admitted!));
        await Assert.That(db.Database.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(await search.SearchAsync(Principal, request)).IsEmpty();
    }

}
