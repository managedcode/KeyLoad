namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlFetchCountFixture
{
    internal static (CommandRequest Command, CommitReceipt Receipt) Seed(TestDatabase database)
    {
        database.Configure(SqlFetchCountProtocol.Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
        [
            new PutDocument(SqlFetchCountProtocol.Collection, "a", """{"label":"a"}"""),
            new PutDocument(SqlFetchCountProtocol.Collection, "b", """{"label":"b"}"""),
            new PutDocument(SqlFetchCountProtocol.Collection, "c", """{"label":"c"}""")
        ]);
        return (command, database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>());
    }

    internal static async Task PolicyAsync(TestDatabase database, long epoch, bool allow)
    {
        var expected = new PrincipalRecord(SqlFetchCountProtocol.Reader, database.Partition.TenantId,
            allow ? [new(database.Partition.DatabaseId, SqlFetchCountProtocol.Collection,
                Capability.Query | Capability.DocumentsRead)] : [], [])
        {
            PolicyEpoch = epoch
        };
        var actual = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
