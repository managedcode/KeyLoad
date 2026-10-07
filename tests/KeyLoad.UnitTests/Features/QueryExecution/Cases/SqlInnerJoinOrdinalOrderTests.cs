using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinOrdinalOrderTests
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string AstralId = "\U00010000";
    private const string PrivateUseId = "\uE000";
    private const string CustomerId = "customer";
    private const string OrderIdColumn = "order_id";
    private const string CustomerIdColumn = "customer_id";
    private const string CustomerPrimaryKeyColumn = "id";
    private const string CustomerNameColumn = "name";
    private const string Sql = "SELECT l.order_id AS order_id, r.name AS name "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC LIMIT 10";

    [Test]
    public async Task AcJoinUsesOrdinalSqlOrderWhenCanonicalKeyBytesHaveTheOppositeOrder()
    {
        using var database = new TestDatabase();
        Configure(database);
        var astralKey = DocumentStorageKeys.RecordKey(database.Partition, Left, AstralId);
        var privateUseKey = DocumentStorageKeys.RecordKey(database.Partition, Left, PrivateUseId);
        await Assert.That(StringComparer.Ordinal.Compare(AstralId, PrivateUseId)).IsLessThan(0);
        await Assert.That(BinaryKeyComparer.Instance.Compare(astralKey, privateUseKey)).IsGreaterThan(0);
        database.Commit(
            new PutDocument(Left, PrivateUseId, Row(PrivateUseId)),
            new PutDocument(Left, AstralId, Row(AstralId)),
            new PutDocument(Right, CustomerId, "{\"id\":\"customer\",\"name\":\"Name\"}"));

        var page = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .Execute("root", new(database.Partition, Sql, AllowFullScan: true, QueryDialectVersion: 2));

        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual([AstralId, PrivateUseId])).IsTrue();
    }

    private static string Row(string id) => System.Text.Json.JsonSerializer.Serialize(
        new { order_id = id, customer_id = CustomerId }, JsonDefaults.Options);

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
