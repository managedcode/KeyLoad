namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageRf3Protocol
{
    internal const string Collection = "lineage_vertices";
    internal const string EarlyGraph = "lineage-early";
    internal const string LateGraph = "lineage-late";
    internal const string Alpha = "alpha";
    internal const string Beta = "beta";
    internal const string Context = "context";
    internal const string EarlyEdge = "early-ab";
    internal const string LateFirstEdge = "late-ab";
    internal const string LateSecondEdge = "late-bc";
    internal const string Label = "route";
    internal const string LabelPath = "/label";
    internal const string LabelRead = "late-label.read";
    internal const string LabelUse = "late-label.use";
    internal const string Classification = "late-label";
    internal const string Attributes = "{\"label\":\"private-edge-value\"}";
    internal const string AlphaJson = "{\"name\":\"alpha\"}";
    internal const string BetaJson = "{\"name\":\"beta\"}";
    internal const string ContextJson = "{\"name\":\"context\"}";
    internal const string VectorField = "/embedding";
    internal const string Model = "lineage-model";
    internal const string ModelVersion = "1";
    internal const string Denied = "The query requires a protected field-use grant.";
    internal const string TenantPrefix = "graph-lineage-";
    internal const string Database = "lineage-database";
    internal const string Domain = "lineage-domain";
    internal const string Absent = "absent";
    internal const string MissingVertex = "The graph vertex is unavailable.";
    internal const string SafeDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const string GenericFailure = "The database operation could not be completed.";
    internal const string ErrorProperty = "error";
    internal const int Version = 1;
    internal const int Fusion = 60;
    internal const int Depth = 1;
    internal const int Vertices = 3;
    internal const int Edges = 2;
    internal const int ResultLimit = 2;
    internal const int ColdCycles = 2;
    internal const long UseEpoch = 2L;
    internal const long WithdrawnEpoch = 3L;
    internal const long RepairedEpoch = 4L;
    internal static VectorSpace Space
    {
        get;
    } = new(Model, 2, DistanceMetric.Cosine, Model, ModelVersion);
}
