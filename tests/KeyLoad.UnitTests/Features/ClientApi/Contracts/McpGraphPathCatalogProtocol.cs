namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Exact property and route names in the independent shortest-path MCP oracle.</summary>
internal static class McpGraphPathCatalogProtocol
{
    internal const string MaxDepth = "maxDepth";
    internal const string MaxVertices = "maxVertices";
    internal const string MaxEdges = "maxEdges";
    internal const string Labels = "labels";
    internal const string Items = "items";
    internal const string Collection = "collection";
    internal const string Id = "id";
    internal const string Sql = "sql";
    internal const string Parameters = "parameters";
    internal const string AllowFullScan = "allowFullScan";
    internal const string Cursor = "cursor";
    internal const string Found = "found";
    internal const string Hops = "hops";
    internal const string Vertices = "vertices";
    internal const string Edges = "edges";
    internal const string CutPosition = "cutPosition";
    internal const string Label = "label";
    internal const string AttributesJson = "attributesJson";
    internal const string Revision = "revision";
    internal const string TenantId = "tenantId";
    internal const string DatabaseId = "databaseId";
    internal const string TransactionDomainId = "transactionDomainId";
    internal const string PartitionKey = "partitionKey";
    internal const string AtomicPartitionId = "atomicPartitionId";
    internal const string GraphRoute = "/v1/graph/shortest-path";
    internal const string SqlRoute = "/v1/query/graph-path";
}
