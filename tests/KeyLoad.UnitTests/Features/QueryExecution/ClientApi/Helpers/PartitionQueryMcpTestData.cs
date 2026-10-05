using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryMcpTestData
{
    internal const string Tenant = "mcp-query-tenant";
    internal const string Database = "mcp-query-database";
    internal const string Domain = "mcp-query-domain";
    internal const string PartitionKey = "mcp-query-partition";
    internal const string Collection = "mcp-query-records";
    internal const string EntityId = "mcp-query-entity";
    internal const string Json = "{\"name\":\"value\"}";
    internal const string AccessPath = "zone-tree-index";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);

    internal static PartitionQueryRequestV1 Request() => new(1, [Partition],
        new SelectQuery(Collection, null, [new Selection("/name", "name")], null, [], 4),
        null, true, 1);

    internal static PartitionQueryPageV1 Page()
    {
        var reference = new EntityRef(Partition, Collection, EntityId);
        var row = new QueryRow(EntityId, 7, Json, false, ImmutableArray<string>.Empty);
        var witness = new PartitionQueryLeafWitnessV1(Partition, 19, 3, 2, AccessPath);
        return new PartitionQueryPageV1(1, [new PartitionQueryRowV1(reference, row)], [witness], true);
    }
}
