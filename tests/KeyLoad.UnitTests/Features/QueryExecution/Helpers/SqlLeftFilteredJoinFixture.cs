using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlLeftFilteredJoinFixture
{
    private const string OrderPrimaryKey = "order_id";
    private const string CustomerPrimaryKey = "id";
    private const string MinimumParameter = "minimum";

    internal const string Left = "filteredorders";
    internal const string Right = "filteredcustomers";
    internal const string Reader = "filtered-reader";
    internal const string UseGrant = "filter.use";
    internal const string FilterReadGrant = "filter.read";
    internal const string ReadGrant = "name.read";
    internal const string Prefix = "SELECT TOP (10) l.order_id AS order_id, r.name AS customer_name, l.total AS total "
        + "FROM filteredorders AS l INNER JOIN filteredcustomers AS r ON l.customer_id = r.id ";
    internal const string Order = "ORDER BY l.order_id ASC";
    internal const string ParameterSql = Prefix + "WHERE l.total >= @minimum AND NOT (l.total IS NULL) " + Order;
    internal const string LiteralSql = Prefix + "WHERE l.total = 7 " + Order;
    internal const string FirstJson = "{\"order_id\":\"a\",\"customer_id\":\"c1\",\"total\":7}";
    internal const string SecondJson = "{\"order_id\":\"b\",\"customer_id\":\"c2\",\"total\":2}";
    internal const string FirstRight = "{\"id\":\"c1\",\"name\":\"Ada\"}";
    internal const string SecondRight = "{\"id\":\"c2\",\"name\":\"Lin\"}";

    internal static (CommandRequest Command, CommitReceipt Receipt) Seed(TestDatabase database)
    {
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Left, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(OrderPrimaryKey, [new(OrderPrimaryKey, RelationalColumnType.Text),
                    new("customer_id", RelationalColumnType.Text), new("total", RelationalColumnType.WholeNumber)]),
                FieldPolicies = [new("/total", "filter", RawReadGrant: FilterReadGrant, RawUseGrant: UseGrant)]
            }));
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(Right, ResourceKind.Collection,
                database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(CustomerPrimaryKey, [new(CustomerPrimaryKey, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]),
                FieldPolicies = [new("/name", "private", RawReadGrant: ReadGrant)]
            }));
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(Left, "a", FirstJson), new PutDocument(Left, "b", SecondJson),
                new PutDocument(Right, "c1", FirstRight), new PutDocument(Right, "c2", SecondRight)]);
        var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        return (command, receipt);
    }

    internal static QueryRequest Request(PartitionRef partition, string? sql = null)
        => new(partition, sql ?? ParameterSql,
            Parameters: new() { [MinimumParameter] = System.Text.Json.JsonSerializer.SerializeToElement(7) },
            AllowFullScan: true, QueryDialectVersion: 2);

    internal static AstQueryRequest Ast(PartitionRef partition)
        => new(partition, new SelectQuery(Left, "l",
            [new("/order_id", OrderPrimaryKey, "l"), new("/name", "customer_name", "r"), new("/total", "total", "l")],
            new Logical(new Comparison(new FieldOperand("/total"), ">=", new ParameterOperand(MinimumParameter)),
                "AND", new Negation(new NullTest(new FieldOperand("/total"), false, false))),
            [new("/order_id", false)], 10, InnerJoin: new(Right, "r", "/customer_id", "/id")),
            Parameters: new() { [MinimumParameter] = System.Text.Json.JsonSerializer.SerializeToElement(7) },
            AllowFullScan: true, AstVersion: 2);
}
