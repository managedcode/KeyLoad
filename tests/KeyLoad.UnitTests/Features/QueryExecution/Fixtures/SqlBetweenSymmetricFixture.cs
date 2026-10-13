namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricFixture
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

}
