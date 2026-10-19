namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinPrimaryIdentityFixture
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string OrderId = "order_id";
    private const string CustomerId = "customer_id";
    private const string Id = "id";
    private const string Total = "total";
    private const string Name = "name";
    private const string FirstLeft = "a";
    private const string LastLeft = "z";
    private const string FirstRight = "c1";
    private const string LastRight = "c2";
    private const string FirstOrder = """{"order_id":"a","customer_id":"c1","total":7}""";
    private const string LastOrder = """{"order_id":"z","customer_id":"c2","total":8}""";
    private const string FirstCustomer = """{"id":"c1","name":"7"}""";
    private const string LastCustomer = """{"id":"c2","name":"8"}""";
    private const string ForeignOrder = """{"order_id":"foreign","customer_id":"c2","total":8}""";
    private const string MissingOrder = """{"customer_id":"c2","total":8}""";
    private const string NonTextOrder = """{"order_id":7,"customer_id":"c2","total":8}""";
    private const string ForeignCustomer = """{"id":"foreign","name":"8"}""";
    private const string MissingCustomer = """{"name":"8"}""";
    private const string NonTextCustomer = """{"id":7,"name":"8"}""";
    private const string DuplicateOrder = """{"order_id":"foreign","order_id":"z","customer_id":"c2","total":8}""";
    private const string DuplicateCustomer = """{"id":"foreign","id":"c2","name":"8"}""";
    private const string MissingOriginal = "The original retained primary identity row is absent.";
    private const string MalformedRow = "{";
    private const string NonObjectRow = "[]";

    internal static (global::KeyLoad.CommandRequest Command, global::KeyLoad.CommitReceipt Receipt) Seed(TestDatabase database)
    {
        database.Submit(global::KeyLoad.OperationKind.ConfigureResource, new global::KeyLoad.ConfigureResourceRequest(
            database.Partition.TenantId, database.Partition.DatabaseId,
            new global::KeyLoad.ResourceDefinition(Left, global::KeyLoad.ResourceKind.Collection, database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(OrderId, [new(OrderId, global::KeyLoad.RelationalColumnType.Text),
                new(CustomerId, global::KeyLoad.RelationalColumnType.Text), new(Total, global::KeyLoad.RelationalColumnType.WholeNumber)])
            }));
        database.Submit(global::KeyLoad.OperationKind.ConfigureResource, new global::KeyLoad.ConfigureResourceRequest(
            database.Partition.TenantId, database.Partition.DatabaseId,
            new global::KeyLoad.ResourceDefinition(Right, global::KeyLoad.ResourceKind.Collection, database.Partition.TransactionDomainId)
            {
                RelationalSchema = new(Id, [new(Id, global::KeyLoad.RelationalColumnType.Text), new(Name, global::KeyLoad.RelationalColumnType.Text)])
            }));
        var id = Guid.NewGuid();
        var command = new global::KeyLoad.CommandRequest(id, database.Partition,
            [new global::KeyLoad.PutDocument(Left, FirstLeft, FirstOrder), new global::KeyLoad.PutDocument(Left, LastLeft, LastOrder),
             new global::KeyLoad.PutDocument(Right, FirstRight, FirstCustomer), new global::KeyLoad.PutDocument(Right, LastRight, LastCustomer)]);
        return (command, database.Submit(global::KeyLoad.OperationKind.Batch, command, id: id).Get<global::KeyLoad.CommitReceipt>());
    }

    internal static SqlInnerJoinIdentityFault[] Capture(TestDatabase database)
    {
        var faults = new List<SqlInnerJoinIdentityFault>();
        foreach (var left in new[] { true, false })
        {
            var key = global::KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(database.Partition,
                left ? Left : Right, left ? LastLeft : LastRight);
            var bytes = database.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingOriginal);
            var row = global::KeyLoad.OrleansSerialization.Deserialize<global::KeyLoad.DocumentRecord>(bytes);
            foreach (var json in left ? new[] { ForeignOrder, MissingOrder, NonTextOrder, MalformedRow, NonObjectRow, DuplicateOrder }
                : new[] { ForeignCustomer, MissingCustomer, NonTextCustomer, MalformedRow, NonObjectRow, DuplicateCustomer })
            {
                faults.Add(new(key, bytes, global::KeyLoad.OrleansSerialization.Serialize(row with
                {
                    Json = json
                }), left));
            }
        }
        return [.. faults];
    }
}
