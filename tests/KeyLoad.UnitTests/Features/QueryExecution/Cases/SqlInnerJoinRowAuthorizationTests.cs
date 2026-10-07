using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinRowAuthorizationTests
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string OrderIdColumn = "order_id";
    private const string CustomerIdColumn = "customer_id";
    private const string CustomerPrimaryKeyColumn = "id";
    private const string CustomerNameColumn = "name";
    private const string Sql = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";
    private const int Dialect = 2;

    [Test]
    public async Task AcJoinOmitsRowsDeniedOnEitherSideAndSucceedsAfterPersistedOwnershipChanges()
    {
        using var database = new TestDatabase();
        Configure(database);
        database.Commit(new PutDocument(Left, "a", "{\"order_id\":\"a\",\"customer_id\":\"c1\"}", Access: new("other")),
            new PutDocument(Left, "z", "{\"order_id\":\"z\",\"customer_id\":\"c2\"}", Access: new("reader")),
            new PutDocument(Right, "c1", "{\"id\":\"c1\",\"name\":\"Ada\"}", Access: new("reader")),
            new PutDocument(Right, "c2", "{\"id\":\"c2\",\"name\":\"Lin\"}", Access: new("other")));
        var reader = new PrincipalRecord("reader", database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Left, Capability.Query | Capability.DocumentsRead),
                new(database.Partition.DatabaseId, Right, Capability.Query | Capability.DocumentsRead)], [])
        { RestrictRows = true, OwnerId = "reader" };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        var beforeDeniedRead = database.Store.Position;
        var deniedRows = engine.Execute("reader", new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect));
        await Assert.That(database.Store.Position).IsEqualTo(beforeDeniedRead);
        var administrativeRows = engine.Execute("root", new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect));

        await Assert.That(deniedRows.Rows).IsEmpty();
        await Assert.That(administrativeRows.Rows.Select(row => row.EntityId)).IsEquivalentTo(["a", "z"], CollectionOrdering.Matching);
        database.Commit(new PutDocument(Left, "a", "{\"order_id\":\"a\",\"customer_id\":\"c1\"}", 1, new("reader")),
            new PutDocument(Right, "c2", "{\"id\":\"c2\",\"name\":\"Lin\"}", 1, new("reader")));
        var healthy = engine.Execute("reader", new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect));
        await Assert.That(healthy.Rows.Select(row => row.EntityId)).IsEquivalentTo(["a", "z"], CollectionOrdering.Matching);
    }

    private static void Configure(TestDatabase database)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Left, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(OrderIdColumn, [new(OrderIdColumn, RelationalColumnType.Text), new(CustomerIdColumn, RelationalColumnType.Text)]) }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Right, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = new(CustomerPrimaryKeyColumn, [new(CustomerPrimaryKeyColumn, RelationalColumnType.Text), new(CustomerNameColumn, RelationalColumnType.Text)]) }));
    }
}
