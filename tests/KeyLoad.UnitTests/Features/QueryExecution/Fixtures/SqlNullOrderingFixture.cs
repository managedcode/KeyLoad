namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullOrderingFixture
{
    internal static (CommandRequest Command, CommitReceipt Receipt) Seed(TestDatabase database)
    {
        database.Configure(SqlNullOrderingProtocol.Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
        [
            new PutDocument(SqlNullOrderingProtocol.Collection, "missing", """{"label":"missing"}"""),
            new PutDocument(SqlNullOrderingProtocol.Collection, "null_a", """{"label":"null_a","score":null}"""),
            new PutDocument(SqlNullOrderingProtocol.Collection, "null_b", """{"label":"null_b","score":null}"""),
            new PutDocument(SqlNullOrderingProtocol.Collection, "low", """{"label":"low","score":1}"""),
            new PutDocument(SqlNullOrderingProtocol.Collection, "high", """{"label":"high","score":2}""")
        ]);
        return (command, database.Submit(OperationKind.Batch, command, id: id).Get<CommitReceipt>());
    }

    internal static async Task PolicyAsync(TestDatabase database, long epoch, bool allow)
    {
        var expected = new PrincipalRecord(SqlNullOrderingProtocol.Reader, database.Partition.TenantId,
            allow ? [new(database.Partition.DatabaseId, SqlNullOrderingProtocol.Collection,
                Capability.Query | Capability.DocumentsRead)] : [], [])
        {
            PolicyEpoch = epoch
        };
        var actual = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
