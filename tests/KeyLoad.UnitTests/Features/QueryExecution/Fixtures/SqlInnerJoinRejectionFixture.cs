using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinRejectionFixture
{
    internal const string HealthySql = "SELECT l.order_id AS order_id, r.name AS customer_name, l.total AS total "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id ORDER BY l.order_id ASC LIMIT 10";
    internal const string SelectFrom = "SELECT l.order_id AS order_id FROM orders AS l ";
    internal const string Join = "INNER JOIN customers AS r ON l.customer_id = r.id ";
    internal const string Order = "ORDER BY l.order_id ASC LIMIT 10";
    internal const string Root = "root";
    internal const string LeftJson = "{\"order_id\":\"order\",\"customer_id\":\"customer\",\"total\":7,\"quantity\":1}";
    internal const string RightJson = "{\"id\":\"customer\",\"name\":\"Ada\",\"alternate\":\"customer\",\"quantity\":1}";
    private const string OrderKey = "order_id";
    private const string CustomerKey = "id";

    internal static void Seed(TestDatabase database)
    {
        Configure(database, "orders", new(OrderKey, [new(OrderKey, RelationalColumnType.Text),
            new("customer_id", RelationalColumnType.Text), new("total", RelationalColumnType.WholeNumber),
            new("quantity", RelationalColumnType.WholeNumber)]));
        Configure(database, "customers", new(CustomerKey, [new(CustomerKey, RelationalColumnType.Text),
            new("name", RelationalColumnType.Text), new("alternate", RelationalColumnType.Text),
            new("quantity", RelationalColumnType.WholeNumber)]));
        database.Configure("untyped", ResourceKind.Collection);
        database.Configure("queue", ResourceKind.WorkQueue);
        database.Configure("foreign", ResourceKind.Collection, "other-domain");
        database.Commit(new PutDocument("orders", "order", LeftJson),
            new PutDocument("customers", "customer", RightJson),
            new PutDocument("untyped", "customer", RightJson));
    }

    internal static AstQueryRequest Ast(TestDatabase database)
        => new(database.Partition, new SelectQuery("orders", "l", [new("/order_id", "order_id", "l")],
            null, [new("/order_id", false)], 10, InnerJoin: new("customers", "r", "/customer_id", "/id")),
            AllowFullScan: true, AstVersion: 2);

    internal static async Task VerifyHealthyAsync(TestDatabase database, QueryEngine engine, byte[] before, long position)
    {
        var page = engine.Execute(Root, new(database.Partition, HealthySql, AllowFullScan: true, QueryDialectVersion: 2));
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-primary-key-inner-join");
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await SqlInnerJoinPageAssertions.AssertRowAsync(page.Rows[0], "order", "Ada", 7, "customer");
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(before)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static void Configure(TestDatabase database, string name, RelationalSchema schema)
        => database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(name, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            { RelationalSchema = schema })).Get<ResourceDefinition>();
}
