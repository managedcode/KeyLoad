namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsMixedPublicProtocol
{
    internal const string Prefix = "analytics-mixed-";
    internal const string DocumentSuffix = "-document";
    internal const string EventSuffix = "-event";
    internal const string EdgeSuffix = "-edge";
    internal const string StreamSuffix = "-stream";
    internal const string Graph = "gate_graph_0";
    internal const string DocumentJson = """{"kind":"analytics-mixed-public"}""";
    internal const string Attributes = """{"kind":"analytics-mixed-public"}""";
    internal const string EventType = "analytics-mixed-public";
    internal const string PutKind = "putDocument";
    internal const string EdgeKind = "upsertEdge";
    internal const string AppendKind = "appendEvents";
    internal const int Empty = 0;
    internal const int Created = 1;
    internal const int MaximumVertices = 2;
    // All writer/store cases retain one anchor adjacency; the reader charges examined edges before filtering labels.
    internal const int MaximumEdges = FunctionalGateProtocol.UserPairs * FunctionalGateProtocol.ResourceCount;
    internal const int PageLimit = 2;
    internal const int MaximumGroups = 1;
    internal const int Year = 2030;
    internal const int Month = 1;
    internal const int Day = 1;
    internal const double Value = 7;
}
