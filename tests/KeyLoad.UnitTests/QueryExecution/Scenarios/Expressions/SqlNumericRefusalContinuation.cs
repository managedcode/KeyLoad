using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlNumericRefusalContinuation
{
    private const string HealthySql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id";
    private const string DecimalEndpointsSql = "SELECT * FROM orders WHERE number BETWEEN -79228162514264337593543950335 AND 79228162514264337593543950335 ORDER BY id";
    private const string IntegerOverflowSql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id LIMIT 2147483648";
    private const string DecimalOverflowSql = "SELECT * FROM orders WHERE number = 79228162514264337593543950336 ORDER BY id";
    private const string NegativeOverflowSql = "SELECT * FROM orders WHERE number = -79228162514264337593543950336 ORDER BY id";
    private const string ExponentOverflowSql = "SELECT * FROM orders WHERE number = 1e100 ORDER BY id";

    internal static async Task RunAsync(TestDatabase db, QueryEngine engine)
    {
        await RefusalsAsync(db, db.Store, engine);
        await SqlNumericRefusalAssertions.HealthyAsync(db, db.Store, engine, DecimalEndpointsSql);
        var bounded = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution(
            new QueryExecutionOptions { MaximumResultBytes = 1 }));
        await SqlNumericRefusalAssertions.RefusedAsync(db, db.Store, bounded, HealthySql, ErrorCode.BudgetExceeded);
        await SqlNumericRefusalAssertions.HealthyAsync(db, db.Store, engine, HealthySql);
        await ColdAsync(db);
    }

    private static async Task RefusalsAsync(TestDatabase db, ZoneTreeStore store, QueryEngine engine)
    {
        foreach (var sql in new[] { IntegerOverflowSql, DecimalOverflowSql, NegativeOverflowSql, ExponentOverflowSql })
        {
            await SqlNumericRefusalAssertions.RefusedAsync(db, store, engine, sql, ErrorCode.Validation);
            await SqlNumericRefusalAssertions.HealthyAsync(db, store, engine, HealthySql);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var image = QueueWholeFlowStorage.Bytes(store);
        var cut = store.Position;
        QueryPage? partial = null;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => partial = engine.Execute(
            SqlBetweenTestData.Root, SqlBetweenTestData.Request(db, DecimalOverflowSql), cancellationToken: cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        await Assert.That(partial).IsNull();
        await SqlNumericRefusalAssertions.UnchangedAsync(store, image, cut);
        await SqlNumericRefusalAssertions.HealthyAsync(db, store, engine, HealthySql);
    }

    private static async Task ColdAsync(TestDatabase db)
    {
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        db.Store.Dispose();
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await SqlNumericRefusalAssertions.UnchangedAsync(cold, image, cut);
                var engine = new QueryEngine(db.CreateConfiguredDatabase(cold), UnitExecutionOptions.QueryExecution());
                await RefusalsAsync(db, cold, engine);
                await SqlNumericRefusalAssertions.HealthyAsync(db, cold, engine, DecimalEndpointsSql);
                await SqlNumericRefusalAssertions.HealthyAsync(db, cold, engine, HealthySql);
                await SqlNumericRefusalAssertions.UnchangedAsync(cold, image, cut);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
