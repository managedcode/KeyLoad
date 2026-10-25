namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlOffsetFixture
{
    internal static (CommandRequest Seed, CommitReceipt Receipt) Seed(TestDatabase database)
    {
        Configure(database, new(SqlOffsetProtocol.Left, ResourceKind.Collection, database.Partition.TransactionDomainId)
        {
            RelationalSchema = new(SqlOffsetProtocol.Order, [new(SqlOffsetProtocol.Order, RelationalColumnType.Text),
            new(SqlOffsetProtocol.Foreign, RelationalColumnType.Text), new(SqlOffsetProtocol.Title, RelationalColumnType.Text)])
        });
        Configure(database, new(SqlOffsetProtocol.Right, ResourceKind.Collection, database.Partition.TransactionDomainId)
        {
            RelationalSchema = new(SqlOffsetProtocol.Id, [new(SqlOffsetProtocol.Id, RelationalColumnType.Text),
            new(SqlOffsetProtocol.Name, RelationalColumnType.Text)])
        });
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
        [
            new PutDocument(SqlOffsetProtocol.Left, "a", """{"order_id":"a","customer_id":"c1","title":"first"}"""),
            new PutDocument(SqlOffsetProtocol.Left, "b", """{"order_id":"b","customer_id":"c2","title":"second"}"""),
            new PutDocument(SqlOffsetProtocol.Left, "c", """{"order_id":"c","customer_id":"c3","title":"third"}"""),
            new PutDocument(SqlOffsetProtocol.Left, "d", """{"order_id":"d","customer_id":"c4","title":"fourth"}"""),
            new PutDocument(SqlOffsetProtocol.Right, "c1", """{"id":"c1","name":"Aye"}"""),
            new PutDocument(SqlOffsetProtocol.Right, "c2", """{"id":"c2","name":"Bee"}"""),
            new PutDocument(SqlOffsetProtocol.Right, "c3", """{"id":"c3","name":"See"}"""),
            new PutDocument(SqlOffsetProtocol.Right, "c4", """{"id":"c4","name":"Dee"}""")
        ]);
        return (command, database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>());
    }
    private static void Configure(TestDatabase database, ResourceDefinition resource)
        => _ = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource)).Get<ResourceDefinition>();

    internal static PrincipalRecord Principal(TestDatabase database, long epoch, bool allow)
        => new(SqlOffsetProtocol.Reader, database.Partition.TenantId, allow
            ? [new(database.Partition.DatabaseId, SqlOffsetProtocol.Left, Capability.Query | Capability.DocumentsRead),
               new(database.Partition.DatabaseId, SqlOffsetProtocol.Right, Capability.Query | Capability.DocumentsRead)] : [], [])
        {
            PolicyEpoch = epoch
        };

    internal static async Task PolicyAsync(TestDatabase database, long epoch, bool allow)
    {
        var expected = Principal(database, epoch, allow);
        var actual = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
