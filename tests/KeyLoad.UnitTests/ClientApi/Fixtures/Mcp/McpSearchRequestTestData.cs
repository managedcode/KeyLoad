namespace KeyLoad.UnitTests.ClientApi;

/// <summary>Complete current search request values for the existing canonical MCP decoder corpus.</summary>
internal static class McpSearchRequestTestData
{
    private const string SpaceId = "mcp-vector-space";
    private const string Model = "mcp-vector-model";
    private const string ModelVersion = "mcp-vector-version";
    private const string NodeIdText = "cda3d63f-67d3-45d4-a10c-38ec3b33031d";
    private const string IncarnationText = "80c066fe-057d-4408-8ab8-f138fa262176";
    private const int Dimension = 2;
    private const int ContractVersion = 1;
    private const long IndexGeneration = 5;
    private const long MinimumPosition = 17;
    private const float FirstVector = 0.25f;
    private const float SecondVector = 0.75f;
    private static readonly Guid NodeId = Guid.Parse(NodeIdText);
    private static readonly PhysicalShardRecord Placement = new(NodeId, Guid.Parse(IncarnationText));
    private static readonly VectorSpace Space = new(SpaceId, Dimension, DistanceMetric.Cosine, Model, ModelVersion);

    internal static AnnMaintenanceRequest Ann() => new(McpCanonicalTestData.StableId, McpCanonicalTestData.Consumer,
        McpCanonicalTestData.Resource, McpCanonicalTestData.FieldPath, Space, IndexGeneration, NodeId, Placement,
        AnnMaintenanceMode.Build)
    {
        MaintenanceProfile = new(McpCanonicalTestData.Resource, McpCanonicalTestData.FieldPath, Space)
    };

    internal static TextIndexMaintenanceRequest Text() => new(McpCanonicalTestData.StableId,
        McpCanonicalTestData.Consumer, McpCanonicalTestData.Resource, McpCanonicalTestData.FieldPath,
        IndexGeneration, NodeId, Placement, TextIndexMaintenanceMode.Build);

    internal static WaitForAnnIndexRequest Wait() => new(McpCanonicalTestData.Partition,
        McpCanonicalTestData.Resource, McpCanonicalTestData.FieldPath, Space, McpCanonicalTestData.Consumer,
        IndexGeneration, new(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition.AtomicPartitionId, MinimumPosition));

    internal static ApproximateSearchRequest Search() => new(ContractVersion,
        new(McpCanonicalTestData.Partition, McpCanonicalTestData.Resource,
            VectorField: McpCanonicalTestData.FieldPath, Vector: [FirstVector, SecondVector], Space: Space),
        McpCanonicalTestData.Consumer, IndexGeneration, AnnPageMode.Approximate);
}
