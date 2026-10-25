namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlJoinSourceBindingFixture
{
    internal const string Typed = "typed";
    internal const string Queue = "queue";
    internal const string Events = "events";
    internal const string Root = "root";
    internal const string Unbound = "s";
    private const string LeftCollection = "orders";
    private const string RightCollection = "customers";
    private const string PrimaryKey = "order_id";
    private const string ForeignKey = "customer_id";
    private const string TargetKey = "id";
    private const string Name = "name";
    private const string Total = "total";
    private const string Quantity = "quantity";
    private const string OrderEntity = "order";
    private const string CustomerEntity = "customer";
    private const string TargetJson = """{"id":"customer","name":"Ada"}""";
    private const string ModelRightAlias = "d";
    private const string OrderPath = "/order_id";
    private const string ForeignPath = "/customer_id";
    private const string TargetPath = "/id";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string NamePath = "/name";
    private const string NameOutput = "customer_name";
    private const string Sql = "SELECT TOP (10) r.name AS customer_name FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id ORDER BY l.order_id ASC";
    private const string ModelSelect = "SELECT TOP (10) d.name AS customer_name FROM ";
    private const int Dialect = SqlOperationProtocol.DefaultQueryDialectVersion;
    private const int Limit = 10;

    internal static (global::KeyLoad.CommandRequest Command, global::KeyLoad.CommitReceipt Receipt) Seed(TestDatabase database, string kind)
    {
        if (kind == Queue)
        {
            return SqlQueueReferenceJoinFixture.Seed(database);
        }
        if (kind == Events)
        {
            return SqlEventReferenceJoinFixture.Seed(database);
        }
        database.Submit(global::KeyLoad.OperationKind.ConfigureResource, new global::KeyLoad.ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new global::KeyLoad.ResourceDefinition(LeftCollection, global::KeyLoad.ResourceKind.Collection, database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(PrimaryKey, [new(PrimaryKey, global::KeyLoad.RelationalColumnType.Text), new(ForeignKey, global::KeyLoad.RelationalColumnType.Text),
                new(Total, global::KeyLoad.RelationalColumnType.WholeNumber), new(Quantity, global::KeyLoad.RelationalColumnType.WholeNumber)])
            })).Get<global::KeyLoad.ResourceDefinition>();
        database.Submit(global::KeyLoad.OperationKind.ConfigureResource, new global::KeyLoad.ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new global::KeyLoad.ResourceDefinition(RightCollection, global::KeyLoad.ResourceKind.Collection, database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(TargetKey, [new(TargetKey, global::KeyLoad.RelationalColumnType.Text), new(Name, global::KeyLoad.RelationalColumnType.Text)])
            })).Get<global::KeyLoad.ResourceDefinition>();
        var id = Guid.NewGuid();
        var command = new global::KeyLoad.CommandRequest(id, database.Partition,
            [new global::KeyLoad.PutDocument(LeftCollection, OrderEntity, SqlInnerJoinRejectionFixture.LeftJson),
             new global::KeyLoad.PutDocument(RightCollection, CustomerEntity, TargetJson)]);
        return (command, database.Submit(global::KeyLoad.OperationKind.Batch, command, id: id).Get<global::KeyLoad.CommitReceipt>());
    }

    internal static global::KeyLoad.Query.AstQueryRequest Ast(global::KeyLoad.PartitionRef partition, string kind)
    {
        if (kind == Typed)
        {
            return new(partition, new global::KeyLoad.Query.SelectQuery(LeftCollection, LeftAlias, [new(NamePath, NameOutput, RightAlias)],
                null, [new(OrderPath, false)], Limit, InnerJoin: new(RightCollection, RightAlias, ForeignPath, TargetPath)),
                AllowFullScan: true, AstVersion: Dialect);
        }
        var source = kind == Events ? SqlEventReferenceJoinFixture.Ast(partition) : SqlQueueReferenceJoinFixture.Ast(partition);
        return source with
        {
            Query = source.Query with
            {
                Projection = [new(NamePath, NameOutput, ModelRightAlias)]
            }
        };
    }

    internal static global::KeyLoad.QueryRequest Request(global::KeyLoad.PartitionRef partition, string kind)
    {
        if (kind == Typed)
        {
            return new(partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect);
        }
        var request = kind == Events ? SqlEventReferenceJoinFixture.Request(partition) : SqlQueueReferenceJoinFixture.Request(partition);
        var prefix = kind == Events ? SqlEventReferenceJoinFixture.Prefix : SqlQueueReferenceJoinFixture.Prefix;
        return request with
        {
            Sql = request.Sql.Replace(prefix, ModelSelect, StringComparison.Ordinal)
        };
    }
}
