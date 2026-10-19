using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class CapabilityOutputFixture
{
    internal const string Collection = "manifest_output";
    internal const string Reader = "manifest-output-reader";
    internal const int RuntimeBytes = 8192;
    internal const int RequestBytes = 32768;
    internal const int LowerBytes = 4096;
    internal const int LargerBytes = 16384;
    internal static QueryExecutionOptions Options(int mode) => new()
    {
        MaximumResultBytes = mode switch
        {
            0 => null,
            1 => LowerBytes,
            2 => LargerBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        }
    };
    internal static int ExpectedBytes(int mode) => mode == 1 ? LowerBytes : RuntimeBytes;
    internal static (CommandRequest Command, CommitReceipt Receipt)[] Seed(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        return new[] { "a", "b" }.Select(id =>
        {
            var command = new CommandRequest(Guid.NewGuid(), database.Partition,
                [new PutDocument(Collection, id, "{\"id\":\"" + id + "\",\"payload\":\"" + new string('x', 5000) + "\"}")]);
            var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            return (command, receipt);
        }).ToArray();
    }
    internal static void Policy(TestDatabase database, bool query, long epoch)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader,
            database.Partition.TenantId, [new(database.Partition.DatabaseId, Collection,
                Capability.DocumentsRead | (query ? Capability.Query : Capability.None))], [])
        {
            PolicyEpoch = epoch
        }))
            .Get<PrincipalRecord>();
    internal static QueryRequest Sql(PartitionRef partition, bool compact, int version)
        => new(partition, "SELECT TOP (2) " + (compact ? "id AS [key]" : "payload AS payload")
            + " FROM " + Collection + " ORDER BY id ASC", AllowFullScan: true, QueryDialectVersion: version);
    internal static AstQueryRequest Ast(PartitionRef partition, bool compact, int version)
        => new(partition, new(Collection, null, [new(compact ? "/@id" : "/payload", compact ? "key" : "payload")],
            null, [new("/@id", false)], 2), AllowFullScan: true, AstVersion: version);
}
