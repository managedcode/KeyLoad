namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateQueueGraphProtocol
{
    internal const string Graph = "gate_graph_0";
    internal const string FromPrefix = "qg-from-";
    internal const string ToPrefix = "qg-to-";
    internal const string SourcePrefix = "s-";
    internal const string EdgePrefix = "e-";
    internal const string ReturnPrefix = "r-";
    internal const string Separator = "-";
    internal const string GuidFormat = "N";
    internal const string Label = "gate-queue-graph";
    internal const string Attributes = "{\"flow\":\"queue-graph-queue\"}";
    internal const string DocumentJson = "{\"kind\":\"queue-graph-vertex\"}";
    internal const string EmptyHeaders = "{}";
    internal const string PutKind = "putDocument";
    internal const string DeleteKind = "deleteDocument";
    internal const string EdgeKind = "upsertEdge";
    internal const string UnlinkKind = "deleteEdge";
    internal const string EnqueueKind = "enqueue";
    internal const string AckKind = "Ack";
    internal const int SourceQueue = 0;
    internal const int TargetQueue = 1;
    internal const int MaximumMessages = 1;
    internal const int Depth = 1;
    internal const int MaximumVertices = 2;
    internal const int MaximumEdges = 1;
    internal const long Absent = 0;
    internal const long Created = 1;
    internal const long Deleted = 2;
    internal const long Acknowledged = 3;
}
