using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryPublicTestSupport : IDisposable
{
    internal const string Collection = "public-partition-query";
    internal const string Tenant = "tenant";
    internal const string DatabaseId = "database";
    internal const string Domain = "orders";
    internal const string FirstKey = "partition-a";
    internal const string SecondKey = "partition-b";
    internal const string ReaderId = "public-query-reader";
    private static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private readonly TestDatabase fixture;

    internal PartitionQueryPublicTestSupport(DatabaseLimits? limits = null, SensitiveFieldPolicy[]? fields = null)
    {
        fixture = new TestDatabase(limits);
        fixture.Submit(OperationKind.BootstrapPhysicalShardCatalog,
            new BootstrapPhysicalShardCatalogRequest(1, 0, ShardId, Incarnation,
                PhysicalShardCatalogVoterIds.Standard));
        fixture.Configure(Collection, ResourceKind.Collection, fields: fields);
    }

    internal DatabaseEngine Database => fixture.Database;
    internal PartitionRef First => fixture.Partition with { PartitionKey = FirstKey };
    internal PartitionRef Second => fixture.Partition with { PartitionKey = SecondKey };
    internal static PhysicalShardRecord ExpectedOwner => new(ShardId, Incarnation,
        PhysicalShardCatalogVoterIds.Standard, 1);

    internal static PartitionQueryRequestV1 Request(ImmutableArray<PartitionRef> partitions, int limit = 8,
        SelectQuery? query = null)
        => new(1, partitions, query ?? Query(limit), null, true, 1);

    internal static SelectQuery Query(int limit = 8)
        => new(Collection, null, [new("/label", "label")], null,
            [new("/score", true)], limit);

    internal void AddRows(PartitionRef partition, params PartitionQueryPublicSeed[] rows)
    {
        var mutations = rows.Select(row => (Mutation)new PutDocument(Collection, row.Id,
            JsonSerializer.Serialize(new { score = row.Score, label = row.Label, secret = row.Secret },
                JsonDefaults.Options), Access: row.Access)).ToImmutableArray();
        var id = Guid.NewGuid();
        _ = fixture.Submit(OperationKind.Batch, new CommandRequest(id, partition, mutations), id: id)
            .Get<CommitReceipt>();
    }

    internal void ConfigureForeign(PartitionRef partition)
    {
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId);
        _ = fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }

    internal void AddReader(PartitionRef allowed, string? fieldGrant = null)
    {
        var reader = new PrincipalRecord(ReaderId, allowed.TenantId,
            [new(allowed.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)],
            fieldGrant is null ? [] : [fieldGrant]);
        _ = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader))
            .Get<PrincipalRecord>();
    }

    internal long Position => fixture.Store.Position;

    public void Dispose() => fixture.Dispose();
}
