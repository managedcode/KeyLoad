using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinDeclaredProjectionTests
{
    private const string AcceptedSql = "SELECT l.order_id AS order_id, r.id AS customer_id, l.total AS total "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 100";
    private const string DeclaredSql = "SELECT r.id AS declared_id, l.source_revision AS declared_revision, l.order_id AS order_id "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 100";
    private const string ScalarSql = "SELECT l.id AS metadata_id, l.revision AS metadata_revision FROM orders AS l LIMIT 100";
    private const string Orders = "orders";
    private const string Customers = "customers";
    private const string OrderId = "order_id";
    private const string CustomerId = "customer_id";
    private const string Id = "id";
    private const string SourceRevision = "source_revision";
    private const string Total = "total";
    private const string Left = "l";
    private const string Right = "r";
    private const string Entity = "order";
    private const string Customer = "customer";
    private const string LiteralId = Customer;
    private const string DeclaredIdAlias = "declared_id";
    private const string DeclaredRevisionAlias = "declared_revision";
    private const string MetadataIdAlias = "metadata_id";
    private const string MetadataRevisionAlias = "metadata_revision";
    private const string OrderJson = "{\"order_id\":\"order\",\"customer_id\":\"customer\",\"total\":7,\"source_revision\":999}";
    private const string CustomerJson = "{\"id\":\"customer\"}";
    private const int Dialect = 2;
    private const int Limit = 100;
    private const int DeclaredRevision = 999;

    [Test]
    public async Task AcceptedSqlAndDeclaredColumnsUseRealRowsWithExactAstParityAndHealthyQ1Followup()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Orders, Entity, OrderJson), new PutDocument(Customers, Customer, CustomerJson));
        var before = database.Store.Position;
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var accepted = engine.Execute("root", new(database.Partition, AcceptedSql, AllowFullScan: true,
            QueryDialectVersion: Dialect));
        await VerifyParityAsync(engine, database, accepted,
            [new("/order_id", OrderId, Left), new("/id", CustomerId, Right), new("/total", Total, Left)]);
        using var acceptedJson = JsonDocument.Parse(accepted.Rows[0].Json);
        await Assert.That(acceptedJson.RootElement.EnumerateObject().Count()).IsEqualTo(3);
        await Assert.That(acceptedJson.RootElement.GetProperty(OrderId).GetString()).IsEqualTo(Entity);
        await Assert.That(acceptedJson.RootElement.GetProperty(CustomerId).GetString()).IsEqualTo(Customer);
        await Assert.That(acceptedJson.RootElement.GetProperty(Total).GetInt32()).IsEqualTo(7);
        var declared = engine.Execute("root", new(database.Partition, DeclaredSql, AllowFullScan: true,
            QueryDialectVersion: Dialect));
        await VerifyParityAsync(engine, database, declared,
            [new("/id", DeclaredIdAlias, Right), new("/source_revision", DeclaredRevisionAlias, Left), new("/order_id", OrderId, Left)]);
        using var declaredJson = JsonDocument.Parse(declared.Rows[0].Json);
        await Assert.That(declaredJson.RootElement.EnumerateObject().Count()).IsEqualTo(3);
        await Assert.That(declaredJson.RootElement.GetProperty(OrderId).GetString()).IsEqualTo(Entity);
        await Assert.That(declaredJson.RootElement.GetProperty(DeclaredIdAlias).GetString()).IsEqualTo(LiteralId);
        await Assert.That(declaredJson.RootElement.GetProperty(DeclaredRevisionAlias).GetInt32()).IsEqualTo(DeclaredRevision);
        var healthy = engine.Execute("root", new(database.Partition, ScalarSql, AllowFullScan: true));
        await Assert.That(healthy.Rows).HasSingleItem();
        using var healthyJson = JsonDocument.Parse(healthy.Rows[0].Json);
        await Assert.That(healthyJson.RootElement.GetProperty(MetadataIdAlias).GetString()).IsEqualTo(Entity);
        await Assert.That(healthyJson.RootElement.GetProperty(MetadataRevisionAlias).GetInt64()).IsEqualTo(1);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    private static async Task VerifyParityAsync(QueryEngine engine, TestDatabase database, QueryPage page,
        System.Collections.Immutable.ImmutableArray<Selection> selections)
    {
        var ast = new AstQueryRequest(database.Partition,
            new SelectQuery(Orders, Left, selections, null, [new("/order_id", false)], Limit,
                InnerJoin: new(Customers, Right, "/customer_id", "/id")), AllowFullScan: true, AstVersion: Dialect);
        var fromAst = engine.ExecuteAst("root", ast);
        await Assert.That(JsonDefaults.Serialize(fromAst).AsSpan().SequenceEqual(JsonDefaults.Serialize(page))).IsTrue();
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-primary-key-inner-join");
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(database.Store.Position);
        var row = page.Rows[0];
        await Assert.That(row.EntityId).IsEqualTo(Entity);
        await Assert.That(row.Revision).IsEqualTo(1);
        await Assert.That(row.Redacted).IsFalse();
        await Assert.That(row.Sources).IsNotNull();
        var sources = row.Sources!.Value;
        await Assert.That(sources.Length).IsEqualTo(2);
        await Assert.That(sources[0].Alias).IsEqualTo(Left);
        await Assert.That(sources[0].EntityId).IsEqualTo(Entity);
        await Assert.That(sources[0].Revision).IsEqualTo(1);
        await Assert.That(sources[1].Alias).IsEqualTo(Right);
        await Assert.That(sources[1].EntityId).IsEqualTo(Customer);
        await Assert.That(sources[1].Revision).IsEqualTo(1);
    }

    private static void Configure(TestDatabase database)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Orders, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(OrderId, [new(OrderId, RelationalColumnType.Text),
                    new(CustomerId, RelationalColumnType.Text), new(Total, RelationalColumnType.WholeNumber),
                    new(SourceRevision, RelationalColumnType.WholeNumber)])
            })).Get<ResourceDefinition>();
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Customers, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text)])
            })).Get<ResourceDefinition>();
    }
}
