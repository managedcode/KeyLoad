namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlParserAdmissionClusterState(SqlOffsetClusterState Original, CommandRequest Events, CommitReceipt EventReceipt,
    SqlOperationRequest Insert, CommitReceipt InsertReceipt, StreamPage Stream, OutboxStatus Outbox, string RootCredential,
    EntityRefLifetime TableLifetime)
{
    internal PartitionRef Partition => Original.Partition;
    internal EntityRef StoredTyped(string id) => new(Partition, Table, id) { Lifetime = TableLifetime };
    internal const string Secret = "PRIVATE-PARSER-ARGUMENT";
    internal const string EventResource = "parser-events";
    internal const string Table = "parser-typed";
    internal const string TypedJson = """{"id":"typed-row","title":"literal","amount":-1}""";
}
