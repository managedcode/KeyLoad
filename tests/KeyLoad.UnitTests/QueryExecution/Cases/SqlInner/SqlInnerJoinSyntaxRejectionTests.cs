using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlInnerJoinSyntaxRejectionTests
{
    private const string Prefix = SqlInnerJoinRejectionFixture.SelectFrom;
    private const string Join = SqlInnerJoinRejectionFixture.Join;
    private const string Order = SqlInnerJoinRejectionFixture.Order;

    [Test]
    [Arguments(Prefix + "LEFT JOIN customers AS r ON l.customer_id = r.id " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "RIGHT JOIN customers AS r ON l.customer_id = r.id " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "FULL JOIN customers AS r ON l.customer_id = r.id " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "CROSS JOIN customers AS r " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "NATURAL JOIN customers AS r " + Order, ErrorCode.Validation)]
    [Arguments(Prefix + ", customers AS r " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "INNER JOIN customers AS r USING (customer_id) " + Order, ErrorCode.Validation)]
    [Arguments(Prefix + Join + "INNER JOIN untyped AS s ON l.customer_id = s.id " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + Join + "WHERE r.name = 'Ada' " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + Join + "GROUP BY l.order_id", ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT TOP (10) COUNT(l.order_id) AS count FROM orders AS l " + Join + Order, ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT TOP (10) l.order_id AS order_id FROM (SELECT order_id FROM orders) AS l " + Join + Order, ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT TOP (10) * FROM orders AS l " + Join + Order, ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT TOP (10) l.order_id AS repeated, r.name AS repeated FROM orders AS l " + Join + Order, ErrorCode.Validation)]
    [Arguments(Prefix + "INNER JOIN orders AS r ON l.customer_id = r.order_id " + Order, ErrorCode.Validation)]
    [Arguments(Prefix + "INNER JOIN customers AS l ON l.customer_id = l.id " + Order, ErrorCode.Validation)]
    [Arguments(Prefix + "INNER JOIN customers AS r ON r.id = l.customer_id " + Order, ErrorCode.Validation)]
    [Arguments(Prefix + "INNER JOIN customers AS r ON l.customer_id <> r.id " + Order, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + Join + "ORDER BY l.order_id DESC", ErrorCode.Validation)]
    [Arguments(Prefix + Join + "ORDER BY l.total ASC", ErrorCode.Validation)]
    [Arguments(Prefix + Join + "ORDER BY r.id ASC", ErrorCode.Validation)]
    [Arguments(Prefix + Join + "ORDER BY l.order_id ASC, l.total ASC", ErrorCode.Validation)]
    [Arguments(Prefix + Join, ErrorCode.Validation)]
    [Arguments(Prefix + Join + Order + " FOR JSON AUTO", ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT TOP (10) l.order_id AS order_id FROM events('orders', 'stream') AS l " + Join + Order, ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT TOP (10) l.order_id AS order_id FROM queue_messages('queue') AS l " + Join + Order, ErrorCode.UnsupportedCapability)]
    public async Task ClosedSqlFormRejectsWithoutScanOrStateChangeAndSameEngineRemainsHealthy(string sql, ErrorCode code)
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = JsonDefaults.Serialize(engine.Execute(SqlInnerJoinRejectionFixture.Root,
            new(database.Partition, SqlInnerJoinRejectionFixture.HealthySql, AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion)));
        var position = database.Store.Position;
        var diagnostics = database.Store.GetReadDiagnostics();

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlInnerJoinRejectionFixture.Root,
            new(database.Partition, sql, AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion)));

        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsEqualTo(diagnostics.RangeExaminedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await SqlInnerJoinRejectionFixture.VerifyHealthyAsync(database, engine, before, position);
    }
    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NoncurrentSqlDialectRejectsBeforeScanAndCurrentJoinFollows(int dialect)
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = JsonDefaults.Serialize(engine.Execute(SqlInnerJoinRejectionFixture.Root,
            new(database.Partition, SqlInnerJoinRejectionFixture.HealthySql, AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion)));
        var position = database.Store.Position;
        var diagnostics = database.Store.GetReadDiagnostics();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlInnerJoinRejectionFixture.Root,
            new(database.Partition, SqlInnerJoinRejectionFixture.HealthySql, AllowFullScan: true, QueryDialectVersion: dialect)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsEqualTo(diagnostics.RangeExaminedBytes);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await SqlInnerJoinRejectionFixture.VerifyHealthyAsync(database, engine, before, position);
    }
}
