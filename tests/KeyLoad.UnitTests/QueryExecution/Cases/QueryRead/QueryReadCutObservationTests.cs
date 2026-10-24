using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryReadCutObservationTests
{
    [Test]
    public Task ActualObservedAndUnobservedQueriesPreserveCompletePageAndStore() =>
        QueryReadCutObservationCapture.RunAsync(async db =>
        {
            db.Configure("orders", ResourceKind.Collection);
            db.Commit(new PutDocument("orders", "a", "{}"));
            var principal = QueryReadCutObservationCapture.Reader(db, 1);
            string? callbackCut = null;
            var capture = new QueryReadCutObservationCapture
            {
                AfterCapture = () => callbackCut = QueryReadCutObservationCapture.Cut(db)
            };
            var observed = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution(), searchEngine: null, readCutObserver: capture);
            var normal = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
            var request = new QueryRequest(db.Partition, "SELECT * FROM orders", AllowFullScan: true);
            var before = QueryReadCutObservationCapture.Cut(db);
            var page = observed.Execute("reader", request, cancellationToken: TestContext.Current!.Execution.CancellationToken);
            var unchanged = normal.Execute("reader", request);
            await Assert.That(OrleansSerialization.Serialize(page).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(unchanged))).IsTrue();
            await Assert.That(page.Rows.Single().EntityId).IsEqualTo("a");
            await Assert.That(capture.Values.Count).IsEqualTo(1);
            var value = capture.Values.Single();
            await Assert.That(value.NodeId).IsEqualTo(db.Store.Identity.NodeId);
            await Assert.That(value.Incarnation).IsEqualTo(db.Store.Identity.Incarnation);
            await Assert.That(value.ReadGeneration).IsEqualTo(db.Store.Identity.ReadGeneration);
            await Assert.That(value.ResultCutPosition).IsEqualTo(page.CutPosition);
            await Assert.That(value.StorePosition).IsEqualTo(db.Store.Position);
            await Assert.That(value.PrincipalId).IsEqualTo(principal.Id);
            await Assert.That(value.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
            await Assert.That(value.Partition).IsEqualTo(db.Partition);
            await Assert.That(callbackCut).IsEqualTo(before);
            await Assert.That(QueryReadCutObservationCapture.Cut(db)).IsEqualTo(before);
        });

    [Test]
    public Task GenuineOldCursorCutAndNewPhysicalPositionRemainDistinct() =>
        QueryReadCutObservationCapture.RunAsync(async db =>
        {
            db.Configure("orders", ResourceKind.Collection);
            db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
            QueryReadCutObservationCapture.Reader(db, 1);
            var capture = new QueryReadCutObservationCapture();
            var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution(), searchEngine: null, readCutObserver: capture);
            var request = new QueryRequest(db.Partition, "SELECT TOP (1) * FROM orders ORDER BY id", AllowFullScan: true);
            var first = engine.Execute("reader", request);
            await Assert.That(first.Cursor).IsNotNull();
            db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("other", "tenant", [], [])))
                .Get<PrincipalRecord>();
            var before = QueryReadCutObservationCapture.Cut(db);
            var second = engine.Execute("reader", request with
            {
                Cursor = first.Cursor
            });
            var value = capture.Values.Last();
            await Assert.That(second.Rows.Single().EntityId).IsEqualTo("b");
            await Assert.That(value.ResultCutPosition).IsEqualTo(first.CutPosition);
            await Assert.That(value.StorePosition).IsGreaterThan(value.ResultCutPosition);
            await Assert.That(QueryReadCutObservationCapture.Cut(db)).IsEqualTo(before);
        });

    [Test]
    public Task CurrentPersistedRevocationRefusesBeforeObservationAndRestorationIsHealthy() =>
        QueryReadCutObservationCapture.RunAsync(async db =>
        {
            db.Configure("orders", ResourceKind.Collection);
            db.Commit(new PutDocument("orders", "a", "{}"));
            QueryReadCutObservationCapture.Reader(db, 1);
            var capture = new QueryReadCutObservationCapture();
            var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution(), searchEngine: null, readCutObserver: capture);
            var request = new QueryRequest(db.Partition, "SELECT * FROM orders", AllowFullScan: true);
            QueryReadCutObservationCapture.Reader(db, 2, revoked: true);
            var before = QueryReadCutObservationCapture.Cut(db);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("reader", request)).Code)
                .IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That(capture.Values).IsEmpty();
            await Assert.That(QueryReadCutObservationCapture.Cut(db)).IsEqualTo(before);
            var restored = QueryReadCutObservationCapture.Reader(db, 3);
            var healthy = engine.Execute("reader", request);
            await Assert.That(healthy.Rows.Single().EntityId).IsEqualTo("a");
            await Assert.That(capture.Values.Single().PolicyEpoch).IsEqualTo(restored.PolicyEpoch);
        });
}
