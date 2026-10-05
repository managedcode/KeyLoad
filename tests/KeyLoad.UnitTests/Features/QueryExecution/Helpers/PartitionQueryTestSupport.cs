using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryTestSupport
{
    internal const string Collection = "partition-query-orders";
    internal const string Principal = "root";
    internal const string Reader = "partition-query-reader";
    internal const string ForeignTenant = "zz-partition-query-foreign";
    internal const string StatusIndex = "by-status";
    internal const string StatusPath = "/status";
    internal const int ResultLimit = 4;

    internal static TestDatabase Create(DatabaseLimits? limits = null, SensitiveFieldPolicy[]? fields = null)
    {
        var database = new TestDatabase(limits);
        database.Configure(Collection, ResourceKind.Collection, fields: fields);
        return database;
    }

    internal static TestDatabase CreateIndexed(DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        database.Configure(Collection, ResourceKind.Collection,
            indexes: [new(StatusIndex, [StatusPath])]);
        return database;
    }

    internal static PartitionRef[] EqualLengthPartitions(TestDatabase database)
        =>
        [
            database.Partition with { PartitionKey = "leaf-a" },
            database.Partition with { PartitionKey = "leaf-b" },
            database.Partition with { PartitionKey = "leaf-c" }
        ];

    internal static PartitionRef[] Partitions(TestDatabase database)
        =>
        [
            database.Partition,
            database.Partition with { PartitionKey = "leaf-b" },
            database.Partition with { PartitionKey = "leaf-c" }
        ];

    internal static void AddRows(TestDatabase database, PartitionRef partition,
        params PartitionQuerySeed[] rows)
    {
        var commandId = Guid.NewGuid();
        var mutations = rows.Select(row => (Mutation)new PutDocument(Collection, row.Id,
            JsonSerializer.Serialize(new { score = row.Score, label = row.Label, secret = row.Secret }, JsonDefaults.Options))).ToImmutableArray();
        _ = database.Submit(OperationKind.Batch, new CommandRequest(commandId, partition, mutations), id: commandId)
            .Get<CommitReceipt>();
    }

    internal static AstQueryRequest Request(TestDatabase database, int limit = ResultLimit)
    {
        var query = new SelectQuery(Collection, null, [new("/label", "label")], null,
            [new("/score", true)], limit);
        return new(database.Partition, query, AllowFullScan: true);
    }

    internal static AstQueryRequest IndexedRequest(TestDatabase database, int limit)
    {
        var filter = new Comparison(new FieldOperand(StatusPath), "=", ValueOperand.Create("open"));
        var query = new SelectQuery(Collection, null, [new("/label", "label")], filter,
            [new("/score", true)], limit);
        return new(database.Partition, query);
    }

    internal static void AddIndexedRows(TestDatabase database, PartitionRef partition)
    {
        var mutations = ImmutableArray.Create<Mutation>(
            new PutDocument(Collection, "indexed-a", "{\"score\":2,\"label\":\"alpha\",\"status\":\"open\"}"),
            new PutDocument(Collection, "indexed-b", "{\"score\":1,\"label\":\"beta\",\"status\":\"open\"}"),
            new PutDocument(Collection, "indexed-c", "{\"score\":0,\"label\":\"closed\",\"status\":\"closed\"}"));
        var commandId = Guid.NewGuid();
        _ = database.Submit(OperationKind.Batch, new CommandRequest(commandId, partition, mutations), id: commandId)
            .Get<CommitReceipt>();
    }

    internal static void ConfigureForeignPartition(TestDatabase database, PartitionRef partition)
    {
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId);
        _ = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }

    internal static void ConfigureSinglePartitionReader(TestDatabase database)
    {
        var reader = new PrincipalRecord(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []);
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
    }
}

internal sealed record PartitionQuerySeed(string Id, int Score, string Label, string? Secret = null);
