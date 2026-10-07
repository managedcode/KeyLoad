using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinCancellationTests
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string QueryText = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";
    private const int Dialect = 2;
    private const int SeedRows = 512;
    private const int WriteBatchSize = 256;
    private const int PaddingLength = 8_192;
    private const int ExpectedSourceCount = 2;
    private const int ExpectedProjectionFieldCount = 2;
    private const long SeedRevision = 1;
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string OrderIdField = "order_id";
    private const string NameField = "name";
    private const string CustomerPrimaryKeyColumn = "id";
    private const string CustomerId = "customer";
    private const string CustomerName = "Ada";
    private const string DeadlineDiagnostic = "The read execution deadline is exceeded.";
    private static readonly string[] ExpectedLeftIds =
    [
        OrderId(0), OrderId(1), OrderId(10), OrderId(100), OrderId(101),
        OrderId(102), OrderId(103), OrderId(104), OrderId(105), OrderId(106)
    ];

    [Test]
    public async Task AcJoinCancellationAfterNativeRangeProgressSettlesTheOriginalReadAndKeepsTheStoreHealthy()
    {
        using var database = new TestDatabase();
        Configure(database);
        Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(database.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: Dialect);
        var position = database.Store.Position;
        var baseline = database.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        var clock = new CancelAfterNativeRangeProgressTimeProvider(database, baseline, cancellation);
        var query = Task.Run(() => engine.Execute("root", request, clock, cancellation.Token));
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => query);

        await Assert.That(clock.CancellationRequestedAfterNativeProgress).IsTrue();
        await Assert.That(clock.ObservedRangeBytes).IsGreaterThan(baseline.RangeExaminedBytes);
        var cancellationFailure = failure ?? throw new InvalidOperationException();
        await Assert.That(cancellationFailure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = engine.Execute("root", request);
        await AssertHealthyJoinedRowsAsync(healthy);
    }

    [Test]
    public async Task AcJoinDeadlineAfterNativeRangeProgressSettlesTheReadAndKeepsTheStoreHealthy()
    {
        using var database = new TestDatabase();
        Configure(database);
        Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(database.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: Dialect);
        var position = database.Store.Position;
        var baseline = database.Store.GetReadDiagnostics();
        var clock = new ExpireAfterNativeRangeProgressTimeProvider(database, baseline,
            database.Database.Limits.QueryDeadlineSeconds);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", request, clock));

        await Assert.That(clock.ExpiredAfterNativeProgress).IsTrue();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(DeadlineDiagnostic);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = engine.Execute("root", request);
        await AssertHealthyJoinedRowsAsync(healthy);
    }

    private static async Task AssertHealthyJoinedRowsAsync(QueryPage page)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(ExpectedLeftIds.Length);
        for (var index = 0; index < ExpectedLeftIds.Length; index++)
        {
            var row = page.Rows[index];
            var expectedId = ExpectedLeftIds[index];
            await Assert.That(row.EntityId).IsEqualTo(expectedId);
            await Assert.That(row.Revision).IsEqualTo(SeedRevision);
            await Assert.That(row.Redacted).IsFalse();
            await Assert.That(row.Sources.HasValue).IsTrue();
            var sources = row.Sources!.Value;
            await Assert.That(sources.Length).IsEqualTo(ExpectedSourceCount);
            await Assert.That(sources[0].Alias).IsEqualTo(LeftAlias);
            await Assert.That(sources[0].EntityId).IsEqualTo(expectedId);
            await Assert.That(sources[0].Revision).IsEqualTo(SeedRevision);
            await Assert.That(sources[1].Alias).IsEqualTo(RightAlias);
            await Assert.That(sources[1].EntityId).IsEqualTo(CustomerId);
            await Assert.That(sources[1].Revision).IsEqualTo(SeedRevision);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(ExpectedProjectionFieldCount);
            await Assert.That(json.RootElement.GetProperty(OrderIdField).GetString()).IsEqualTo(expectedId);
            await Assert.That(json.RootElement.GetProperty(NameField).GetString()).IsEqualTo(CustomerName);
        }
    }

    private sealed class CancelAfterNativeRangeProgressTimeProvider(TestDatabase database,
        ZoneTreeReadSnapshot baseline, CancellationTokenSource cancellation) : TimeProvider
    {
        private long timestamp;
        private long observedRangeBytes;
        private int cancellationRequested;

        internal bool CancellationRequestedAfterNativeProgress => Volatile.Read(ref cancellationRequested) != 0;
        internal long ObservedRangeBytes => Interlocked.Read(ref observedRangeBytes);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            var rangeBytes = database.Store.GetReadDiagnostics().RangeExaminedBytes;
            if (rangeBytes > baseline.RangeExaminedBytes
                && Interlocked.CompareExchange(ref cancellationRequested, 1, 0) == 0)
            {
                Interlocked.Exchange(ref observedRangeBytes, rangeBytes);
                cancellation.Cancel();
            }
            return Interlocked.Increment(ref timestamp);
        }
    }

    private sealed class ExpireAfterNativeRangeProgressTimeProvider(TestDatabase database,
        ZoneTreeReadSnapshot baseline, int deadlineSeconds) : TimeProvider
    {
        private long timestamp;

        internal bool ExpiredAfterNativeProgress { get; private set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            if (database.Store.GetReadDiagnostics().RangeExaminedBytes > baseline.RangeExaminedBytes)
            {
                ExpiredAfterNativeProgress = true;
                return checked((long)(deadlineSeconds + 1) * TimestampFrequency);
            }
            return Interlocked.Increment(ref timestamp);
        }
    }

    private static void Seed(TestDatabase database)
    {
        var padding = new string('x', PaddingLength);
        for (var offset = 0; offset < SeedRows; offset += WriteBatchSize)
        {
            var mutations = Enumerable.Range(offset, WriteBatchSize).Select(index => (Mutation)new PutDocument(
                Left, OrderId(index), JsonSerializer.Serialize(new { order_id = OrderId(index), customer_id = CustomerId, padding }, JsonDefaults.Options))).ToArray();
            database.Commit(mutations);
        }
        database.Commit(new PutDocument(Right, CustomerId, "{\"id\":\"customer\",\"name\":\"Ada\"}"));
    }

    private static string OrderId(int index) => "order-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Configure(TestDatabase database)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Left, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(OrderIdField, [new(OrderIdField, RelationalColumnType.Text), new("customer_id", RelationalColumnType.Text), new("padding", RelationalColumnType.Text)]) }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Right, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(CustomerPrimaryKeyColumn, [new(CustomerPrimaryKeyColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]) }));
    }
}
