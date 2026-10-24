namespace KeyLoad.UnitTests.Search;

internal static class GraphSearchLineageProtocol
{
    internal const string Root = "root";
    internal const string Reader = "lineage-reader";
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
    internal const string DocumentFamily = "document";
    internal const string WrongRow = "wrong-execution-lineage-vertex-type";
    internal const string Denied = "The query requires a protected field-use grant.";
    internal const string Missing = "The genuine graph-search lineage operation did not settle.";
    internal const int Version = 1;
    internal const int Fusion = 60;
    internal const int Depth = 1;
    internal const int Vertices = 3;
    internal const int Edges = 2;
    internal const int ResultLimit = 2;
    internal const int ColdCycles = 2;
    internal const long InitialEpoch = 1L;
    internal const long UseEpoch = 2L;
    internal const long WithdrawnEpoch = 3L;
    internal const long RepairedEpoch = 4L;
    internal static VectorSpace Space
    {
        get;
    } = new(Model, 2, DistanceMetric.Cosine, Model, ModelVersion);
}
