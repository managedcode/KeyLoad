using KeyLoad.Core;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinBudgetTests
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string HealthyLeft = "healthyorders";
    private const string Sql = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";
    private const string TwoRowSql = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 2";
    private const string ThreeRowSql = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 3";
    private const string HealthySql = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM healthyorders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";
    private const int Dialect = 2;
    private const int ExactWork = 5;
    private const int ExpectedLeftRows = 3;
    private const int ExpectedRightProbes = 3;
    private const int ExpectedFoundRightProbes = 2;
    private const string FirstCustomer = "a";
    private const string LastCustomer = "z";
    private const string MissingCustomer = "absent";
    private const string CustomerIdColumn = "customer_id";
    private const int OverLimit = 3;
    private const string OrderIdColumn = "order_id";
    private const string CustomerPrimaryKeyColumn = "id";

    [Test]
    public async Task AcJoinWorkCountsLeftRecordsAndRightProbesAndLeavesStoreUsableAfterRejection()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Left, "a", "{\"order_id\":\"a\",\"customer_id\":\"a\"}"),
            new PutDocument(Left, "z", "{\"order_id\":\"z\",\"customer_id\":\"z\"}"),
            new PutDocument(Left, "m", "{\"order_id\":\"m\",\"customer_id\":null}"),
            new PutDocument(HealthyLeft, "healthy", "{\"order_id\":\"healthy\",\"customer_id\":\"a\"}"),
            new PutDocument(Right, "a", "{\"id\":\"a\",\"name\":\"Ada\"}"),
            new PutDocument(Right, "z", "{\"id\":\"z\",\"name\":\"Lin\"}"));
        var exact = Engine(database, ExactWork);

        var accepted = exact.Execute("root", new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect));

        await Assert.That(accepted.Rows.Length).IsEqualTo(2);
        database.Commit(new PutDocument(Left, "n", "{\"order_id\":\"n\"}"));
        var beforeRejectedRead = database.Store.Position;
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => exact.Execute("root",
            new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(beforeRejectedRead);

        var healthy = exact.Execute("root", new(database.Partition, HealthySql, AllowFullScan: true, QueryDialectVersion: Dialect));

        await Assert.That(healthy.Rows).HasSingleItem();
        await Assert.That(healthy.Rows[0].EntityId).IsEqualTo("healthy");
    }

    [Test]
    public async Task AcJoinActualZoneTreeReadBudgetAdmitsTheMeasuredScanAndAllRightProbesAtItsExactBoundary()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Left, "a", "{\"order_id\":\"a\",\"customer_id\":\"a\"}"),
            new PutDocument(Left, "m", "{\"order_id\":\"m\",\"customer_id\":\"absent\"}"),
            new PutDocument(Left, "z", "{\"order_id\":\"z\",\"customer_id\":\"z\"}"),
            new PutDocument(Right, "a", "{\"id\":\"a\",\"name\":\"Ada\"}"),
            new PutDocument(Right, "z", "{\"id\":\"z\",\"name\":\"Lin\"}"));
        var measured = SqlInnerJoinNativeReadMeasurement.Measure(database, Left, Right,
            FirstCustomer, MissingCustomer, LastCustomer);
        var exactReadBytes = checked(measured.LeftScanBytes + measured.RightProbeBytes);
        var exact = Engine(database, database.Database.Limits with { MaxQueryReadBytes = exactReadBytes });
        var position = database.Store.Position;

        var accepted = exact.Execute("root", new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect));

        await Assert.That(measured.LeftRows).IsEqualTo(ExpectedLeftRows);
        await Assert.That(measured.RightProbes).IsEqualTo(ExpectedRightProbes);
        await Assert.That(measured.FoundRightProbes).IsEqualTo(ExpectedFoundRightProbes);
        await Assert.That(measured.HasMore).IsFalse();
        await Assert.That(accepted.Rows.Length).IsEqualTo(2);
        var probeBytesOmitted = Engine(database, database.Database.Limits with
        { MaxQueryReadBytes = measured.LeftScanBytes });
        var rejectedProbeBytes = Assert.ThrowsExactly<KeyLoadException>(() => probeBytesOmitted.Execute("root",
            new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect)));
        await Assert.That(rejectedProbeBytes.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(exact.Execute("root", new(database.Partition, Sql, AllowFullScan: true,
            QueryDialectVersion: Dialect)).Rows.Length).IsEqualTo(2);
        var oneByteShort = Engine(database, database.Database.Limits with { MaxQueryReadBytes = exactReadBytes - 1 });
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => oneByteShort.Execute("root",
            new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(exact.Execute("root", new(database.Partition, Sql, AllowFullScan: true,
            QueryDialectVersion: Dialect)).Rows.Length).IsEqualTo(2);
    }

    [Test]
    public async Task AcJoinPageBytesIncludeBothSourcesAndAdmitOnlyTheExactSerializedPageBound()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Left, "a", "{\"order_id\":\"a\",\"customer_id\":\"a\"}"),
            new PutDocument(Left, "z", "{\"order_id\":\"z\",\"customer_id\":\"z\"}"),
            new PutDocument(Right, "a", "{\"id\":\"a\",\"name\":\"Ada\"}"),
            new PutDocument(Right, "z", "{\"id\":\"z\",\"name\":\"Lin\"}"));
        var request = new QueryRequest(database.Partition, TwoRowSql, AllowFullScan: true, QueryDialectVersion: Dialect);
        var measured = Engine(database, database.Database.Limits).Execute("root", request);
        var exactPageBytes = JsonDefaults.Serialize(measured).Length;
        var exact = Engine(database, database.Database.Limits with { MaxBatchBytes = exactPageBytes });

        var accepted = exact.Execute("root", request);

        await Assert.That(accepted.Rows.Length).IsEqualTo(2);
        await Assert.That(accepted.Rows.All(row => row.Sources?.Length == 2)).IsTrue();
        await Assert.That(JsonDefaults.Serialize(accepted).Length).IsEqualTo(exactPageBytes);
        var position = database.Store.Position;
        var oneByteShort = Engine(database, database.Database.Limits with { MaxBatchBytes = exactPageBytes - 1 });
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => oneByteShort.Execute("root", request));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(exact.Execute("root", request))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(accepted))).IsTrue();
    }

    [Test]
    public async Task AcJoinResultCountAdmitsTwoActualRowsAndRejectsTheNextRequestedResultWithHealthyFollowUp()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Left, "a", "{\"order_id\":\"a\",\"customer_id\":\"a\"}"),
            new PutDocument(Left, "z", "{\"order_id\":\"z\",\"customer_id\":\"z\"}"),
            new PutDocument(Right, "a", "{\"id\":\"a\",\"name\":\"Ada\"}"),
            new PutDocument(Right, "z", "{\"id\":\"z\",\"name\":\"Lin\"}"));
        var limits = database.Database.Limits with { MaxResults = 2 };
        var engine = Engine(database, limits);

        var accepted = engine.Execute("root", new(database.Partition, TwoRowSql, AllowFullScan: true, QueryDialectVersion: Dialect));

        await Assert.That(accepted.Rows.Length).IsEqualTo(2);
        var position = database.Store.Position;
        var sqlRejected = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            new(database.Partition, ThreeRowSql, AllowFullScan: true, QueryDialectVersion: Dialect)));
        await Assert.That(sqlRejected.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var astRejected = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            OverLimitAst(database.Partition)));
        await Assert.That(astRejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = engine.Execute("root", new(database.Partition, TwoRowSql, AllowFullScan: true,
            QueryDialectVersion: Dialect));
        await Assert.That(healthy.Rows.Length).IsEqualTo(2);
        await Assert.That(healthy.Rows.Select(row => row.EntityId)).IsEquivalentTo(["a", "z"], CollectionOrdering.Matching);
    }

    private static AstQueryRequest OverLimitAst(PartitionRef partition)
        => new(partition, new SelectQuery(Left, "l",
            [new("/order_id", "order_id", "l"), new("/name", "name", "r")], null,
            [new("/order_id", false)], OverLimit, InnerJoin: new(Right, "r", "/customer_id", "/id")),
            AllowFullScan: true, AstVersion: Dialect);

    private static QueryEngine Engine(TestDatabase database, int maximumWork)
        => Engine(database, database.Database.Limits with { MaxScanRecords = maximumWork });

    private static QueryEngine Engine(TestDatabase database, DatabaseLimits limits)
    {
        var owner = new DatabaseEngine(database.Store, database.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(),
            UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        return new(owner, UnitExecutionOptions.QueryExecution());
    }

    private static void Configure(TestDatabase database)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Left, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(OrderIdColumn, [new(OrderIdColumn, RelationalColumnType.Text), new(CustomerIdColumn, RelationalColumnType.Text, Nullable: true)]) }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(HealthyLeft, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(OrderIdColumn, [new(OrderIdColumn, RelationalColumnType.Text), new(CustomerIdColumn, RelationalColumnType.Text, Nullable: true)]) }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Right, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(CustomerPrimaryKeyColumn, [new(CustomerPrimaryKeyColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]) }));
    }
}
